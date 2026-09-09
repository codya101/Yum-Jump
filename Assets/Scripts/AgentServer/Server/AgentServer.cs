using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace YumJump.Agent
{
    /// <summary>
    /// The Unity half of the agent bridge: a JSON-lines TCP server that exposes the level, the
    /// live state and a tick-quantized plan runner, plus the driver that steps the simulation.
    ///
    /// Stepping model: exactly one tick per rendered frame, and a tick only happens while a plan
    /// or an observe is running. Idle means frozen (invariant I4); one tick per frame means
    /// frame-quantized engine behaviour (Destroy, coroutines) is identical at every speed, which
    /// is what keeps replays honest. Speed is a frame-rate target, not a bigger step.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    public sealed class AgentServer : MonoBehaviour
    {
        // The player payload carries "score" (raw GameManager.score) since 2026-08: an
        // additive field - a client that does not read it sees the payload it always saw, and
        // the Python parser defaults it to 0 against older servers. The constant deliberately
        // does NOT change for it: the client's PlanBook provenance compares this string with
        // strict equality as a stand-in for "same build, same physics", so bumping it orphans
        // every verified plan book even though the physics are untouched. Bump it only for a
        // change that really does invalidate recorded prefixes. A collectible's dynamics entry
        // carries "fruitType" (the FruitType enum name) since 2026-08 on the same terms, and
        // for the same reason does not bump it either.
        public const string ProtocolVersion = "1.0";
        public const int DefaultPort = 7777;

        private sealed class Request
        {
            public int id;
            public string cmd;
            public JsonValue payload;
        }

        [SerializeField] private int port = DefaultPort;
        [SerializeField] private bool logCommands = true;

        private TcpListener listener;
        private Thread listenThread;
        private Thread writeThread;
        private TcpClient client;
        private NetworkStream stream;
        private volatile bool running;

        private readonly ConcurrentQueue<Request> incoming = new ConcurrentQueue<Request>();
        private readonly ConcurrentQueue<string> outgoing = new ConcurrentQueue<string>();
        private readonly AutoResetEvent outgoingSignal = new AutoResetEvent(false);

        private VirtualInput virtualInput;
        private PlanExecutor executor;
        private float restoreListenerVolume = 1f;
        private int restoreVSyncCount = 1;
        private int restoreTargetFrameRate = -1;
        private bool initialised;
        private bool tickedThisFrame;
        private float speedScale = 1f;

        public static AgentServer Instance { get; private set; }

        public int Port => port;

        public void Configure(int listenPort)
        {
            port = listenPort;
        }

        // ------------------------------------------------------------------ lifecycle

        private void Awake()
        {
            Instance = this;

            virtualInput = new VirtualInput();
            executor = new PlanExecutor(virtualInput);

            // Take the world off Unity's clock before anything can move.
            SimClock.EnterManualMode(Time.fixedDeltaTime);
            Physics2D.simulationMode = SimulationMode2D.Script;
            GameInput.Source = virtualInput;

            restoreListenerVolume = AudioListener.volume;
            if (!AgentBootstrap.AudioEnabled)
                AudioListener.volume = 0f;

            restoreVSyncCount = QualitySettings.vSyncCount;
            restoreTargetFrameRate = Application.targetFrameRate;
            ApplySpeed(1f);
        }

        private void Start()
        {
            StartListening();
        }

        private void Update()
        {
            if (!initialised)
            {
                // Every object's Start() has run by the first Update, so the world is now in its
                // authored state and safe to snapshot.
                ResetController.Initialise();
                ResetController.Reset("spawn", out _);
                initialised = true;
                Debug.Log($"[AgentServer] ready on 127.0.0.1:{port} - level '{SceneManager.GetActiveScene().name}', " +
                          $"{SimClock.TickRateHz} Hz ticks, manual stepping.");
            }

            DrainCommands();

            tickedThisFrame = executor.WantsTick();
            if (tickedThisFrame)
            {
                SimClock.BeginTick();
                executor.BeginTick();      // keys applied before any gameplay logic (I2)
                SimRegistry.TickAll();
            }
        }

        private void LateUpdate()
        {
            if (!tickedThisFrame) return;
            tickedThisFrame = false;

            Physics2D.Simulate(SimClock.FixedDelta);
            SimRegistry.SampleVelocities();
            SimClock.EndTick();
            ResetController.ProcessPendingCapture();   // checkpoint snapshots land on tick boundaries
            executor.EndTick();
        }

        private void OnDestroy()
        {
            StopListening();
            GameInput.UseKeyboard();
            AudioListener.volume = restoreListenerVolume;
            QualitySettings.vSyncCount = restoreVSyncCount;
            Application.targetFrameRate = restoreTargetFrameRate;
            SimClock.ExitManualMode();
            Physics2D.simulationMode = SimulationMode2D.FixedUpdate;
            if (Instance == this) Instance = null;
        }

        private void OnApplicationQuit()
        {
            StopListening();
        }

        // ------------------------------------------------------------------ networking

        private void StartListening()
        {
            try
            {
                listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                running = true;

                listenThread = new Thread(ListenLoop) { IsBackground = true, Name = "AgentServer.Listen" };
                listenThread.Start();

                writeThread = new Thread(WriteLoop) { IsBackground = true, Name = "AgentServer.Write" };
                writeThread.Start();
            }
            catch (Exception e)
            {
                Debug.LogError($"[AgentServer] could not listen on port {port}: {e.Message}");
            }
        }

        private void StopListening()
        {
            running = false;
            try { listener?.Stop(); } catch { }
            try { client?.Close(); } catch { }
            outgoingSignal.Set();
            listener = null;
            client = null;
            stream = null;
        }

        private void ListenLoop()
        {
            while (running)
            {
                try
                {
                    TcpClient accepted = listener.AcceptTcpClient();
                    accepted.NoDelay = true;

                    // One client at a time; a new connection replaces the old one.
                    try { client?.Close(); } catch { }
                    client = accepted;
                    stream = accepted.GetStream();

                    ReadLoop(accepted);
                }
                catch (Exception)
                {
                    if (!running) return;
                    Thread.Sleep(50);
                }
            }
        }

        private void ReadLoop(TcpClient connection)
        {
            using (var reader = new StreamReader(connection.GetStream(), Encoding.UTF8))
            {
                while (running && connection.Connected)
                {
                    string line = reader.ReadLine();
                    if (line == null) break;
                    if (line.Length == 0) continue;

                    try
                    {
                        JsonValue msg = JsonValue.Parse(line);
                        incoming.Enqueue(new Request
                        {
                            id = msg["id"].AsInt,
                            cmd = msg["cmd"].AsString ?? "",
                            payload = msg
                        });
                    }
                    catch (Exception e)
                    {
                        Send(new JsonObj().Add("id", 0).Add("error", new JsonObj()
                            .Add("code", "bad_json").Add("message", e.Message)));
                    }
                }
            }
        }

        private void WriteLoop()
        {
            while (running)
            {
                outgoingSignal.WaitOne(200);
                while (outgoing.TryDequeue(out string line))
                {
                    NetworkStream s = stream;
                    if (s == null) continue;
                    try
                    {
                        byte[] bytes = Encoding.UTF8.GetBytes(line + "\n");
                        s.Write(bytes, 0, bytes.Length);
                        s.Flush();
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[AgentServer] write failed: {e.Message}");
                    }
                }
            }
        }

        private void Send(JsonObj message)
        {
            outgoing.Enqueue(Json.Write(message));
            outgoingSignal.Set();
        }

        private void Respond(int id, JsonObj result)
        {
            Send(new JsonObj().Add("id", id).Add("result", result));
        }

        private void RespondError(int id, string code, string message)
        {
            Send(new JsonObj().Add("id", id).Add("error",
                new JsonObj().Add("code", code).Add("message", message)));
        }

        // ------------------------------------------------------------------ dispatch

        private void DrainCommands()
        {
            while (incoming.TryDequeue(out Request request))
            {
                try
                {
                    Handle(request);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    RespondError(request.id, "internal_error", e.Message);
                }
            }
        }

        private void Handle(Request request)
        {
            if (logCommands && request.cmd != "get_state")
                Debug.Log($"[AgentServer] <- {request.cmd} (id {request.id})");

            switch (request.cmd)
            {
                case "hello": HandleHello(request); break;
                case "ping": Respond(request.id, new JsonObj().Add("pong", true).Add("tick", SimClock.Tick)); break;
                case "get_static_map": HandleStaticMap(request); break;
                case "get_state": HandleGetState(request); break;
                case "observe": HandleObserve(request); break;
                case "run_plan": HandleRunPlan(request); break;
                case "reset": HandleReset(request); break;
                case "set_speed": HandleSetSpeed(request); break;
                case "abort": HandleAbort(request); break;
                default:
                    RespondError(request.id, "unknown_command", $"unknown command '{request.cmd}'");
                    break;
            }
        }

        private void HandleHello(Request request)
        {
            Bounds bounds = WorldSampler.HasMap ? WorldSampler.LevelBounds : default;
            var result = new JsonObj()
                .Add("protocolVersion", ProtocolVersion)
                .Add("tickRateHz", SimClock.TickRateHz)
                .Add("keys", new List<object>(VirtualInput.SupportedKeys))
                .Add("levelName", SceneManager.GetActiveScene().name)
                .Add("gridCellSize", WorldSampler.CellSize)
                .Add("speed", speedScale)
                .Add("tick", SimClock.Tick)
                .Add("maxPlanTicks", PlanExecutor.MaxPlanTicks);

            if (WorldSampler.HasMap)
            {
                result.Add("levelBounds", new JsonObj()
                    .Add("min", new Vector2(bounds.min.x, bounds.min.y))
                    .Add("max", new Vector2(bounds.max.x, bounds.max.y)));
            }

            Respond(request.id, result);
        }

        private void HandleStaticMap(Request request)
        {
            float cell = request.payload.Has("cellSize") ? request.payload["cellSize"].AsFloat : WorldSampler.CellSize;
            if (!WorldSampler.HasMap || !Mathf.Approximately(cell, WorldSampler.CellSize))
                WorldSampler.SampleStaticMap(cell);

            var result = new JsonObj()
                .Add("cellSize", WorldSampler.CellSize)
                .Add("origin", WorldSampler.Origin)
                .Add("width", WorldSampler.Width)
                .Add("height", WorldSampler.Height)
                .Add("levelName", SceneManager.GetActiveScene().name)
                .Add("rows", WorldSampler.Rows())
                .Add("legend", WorldSampler.Legend());

            Vector2Int? finish = WorldSampler.FinishCell();
            if (finish.HasValue)
                result.Add("finishCell", new List<object> { finish.Value.x, finish.Value.y });

            result.Add("markers", WorldSampler.Markers());
            result.Add("staticHazards", WorldSampler.StaticHazards());

            // Exact terrain outlines beside the grid, never instead of it: the grid is what the
            // LLM reads, these are what a landing is planned against. Null means the level's
            // geometry blew the point budget, and the client is expected to fall back to the grid.
            List<object> terrain = WorldSampler.Terrain();
            if (terrain != null) result.Add("terrain", terrain);
            else result.Add("terrainOmitted", $"outline exceeds {WorldSampler.MaxTerrainPoints} points");

            Bounds bounds = WorldSampler.LevelBounds;
            result.Add("levelBounds", new JsonObj()
                .Add("min", new Vector2(bounds.min.x, bounds.min.y))
                .Add("max", new Vector2(bounds.max.x, bounds.max.y)));

            Respond(request.id, result);
        }

        private void HandleGetState(Request request)
        {
            int localWidth = request.payload.Has("localWidth") ? request.payload["localWidth"].AsInt : 40;
            int localHeight = request.payload.Has("localHeight") ? request.payload["localHeight"].AsInt : 24;
            float radius = request.payload.Has("radius") ? request.payload["radius"].AsFloat : 0f;
            bool includeGrid = !request.payload.Has("includeGrid") || request.payload["includeGrid"].AsBool;

            Respond(request.id, BuildState(localWidth, localHeight, radius, includeGrid));
        }

        private void HandleObserve(Request request)
        {
            if (executor.Busy)
            {
                RespondError(request.id, "busy", "a plan or observe is already running");
                return;
            }

            int ticks = Mathf.Clamp(request.payload["ticks"].AsInt, 1, PlanExecutor.MaxObserveTicks);
            int id = request.id;
            string warning = QuiescenceWarning();

            executor.StartObserve(ticks, result =>
            {
                if (warning != null) result.Add("warning", warning);
                result.Add("player", BuildPlayer());
                Respond(id, result);
            });
        }

        private void HandleRunPlan(Request request)
        {
            if (executor.Busy)
            {
                RespondError(request.id, "busy", "a plan or observe is already running");
                return;
            }

            List<PlanStep> plan = new List<PlanStep>();
            JsonValue steps = request.payload["plan"];
            int total = 0;

            foreach (JsonValue step in steps.AsArray)
            {
                var keys = new List<string>();
                foreach (JsonValue key in step["keys"].AsArray)
                {
                    string k = key.AsString;
                    if (!VirtualInput.IsSupported(k))
                    {
                        RespondError(request.id, "bad_key", $"unsupported key '{k}'");
                        return;
                    }
                    keys.Add(k);
                }

                int ticks = step["ticks"].AsInt;
                if (ticks < 1)
                {
                    RespondError(request.id, "bad_plan", "every step needs ticks >= 1");
                    return;
                }

                total += ticks;
                plan.Add(new PlanStep { keys = keys.ToArray(), ticks = ticks });
            }

            if (plan.Count == 0)
            {
                RespondError(request.id, "bad_plan", "plan is empty");
                return;
            }

            if (total > PlanExecutor.MaxPlanTicks)
            {
                RespondError(request.id, "bad_plan",
                    $"plan is {total} ticks, limit is {PlanExecutor.MaxPlanTicks}");
                return;
            }

            if (GameManager.instance == null || GameManager.instance.player == null)
            {
                RespondError(request.id, "no_player", "the player is dead - reset before running a plan");
                return;
            }

            int id = request.id;
            executor.StartPlan(plan, result =>
            {
                result.Add("finalState", BuildState(40, 24, 0f, true));
                Respond(id, result);
            });
        }

        private void HandleAbort(Request request)
        {
            // The cut-short job answers here, on the aborting request's id, rather than on its own:
            // the client matches responses by id, so a second one for the plan's id would be a
            // stray. Its partial trace is still the client's learning signal, so it rides along.
            JsonObj job = executor.Abort("aborted by client");

            var result = new JsonObj()
                .Add("aborted", job != null)
                .Add("tick", SimClock.Tick);
            if (job != null) result.Add("job", job);

            Respond(request.id, result);
        }

        private void HandleReset(Request request)
        {
            string target = request.payload.Has("to") ? request.payload["to"].AsString : "spawn";

            // The world the aborted job was running in is about to be thrown away, so its partial
            // result is dropped: this request answers with the reset state instead.
            executor.Abort("reset");

            if (!ResetController.Reset(target, out string error))
            {
                RespondError(request.id, "bad_reset", error);
                return;
            }

            JsonObj state = BuildState(40, 24, 0f, true);
            state.Add("resetTo", target ?? "spawn");
            Respond(request.id, state);
        }

        private void HandleSetSpeed(Request request)
        {
            float scale = request.payload["scale"].AsFloat;
            if (scale <= 0f || scale > 8f)
            {
                RespondError(request.id, "bad_speed", "scale must be in (0, 8]");
                return;
            }

            ApplySpeed(scale);
            Respond(request.id, new JsonObj()
                .Add("scale", speedScale)
                .Add("targetFrameRate", Application.targetFrameRate));
        }

        private void ApplySpeed(float scale)
        {
            speedScale = scale;
            // One tick per frame, so the frame-rate target *is* the sim speed. Plans are counted
            // in ticks, so this changes wall-clock time only - never the outcome.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = Mathf.RoundToInt(SimClock.TickRateHz * speedScale);
        }

        // ------------------------------------------------------------------ state building

        private JsonObj BuildState(int localWidth, int localHeight, float radius, bool includeGrid)
        {
            Player player = GameManager.instance != null ? GameManager.instance.player : null;
            Vector2 center = player != null ? player.SimPosition : Vector2.zero;

            // Sample the level on first use so a client that never calls get_static_map still
            // gets a local grid.
            if (includeGrid && !WorldSampler.HasMap)
                WorldSampler.SampleStaticMap(WorldSampler.CellSize);

            var result = new JsonObj()
                .Add("tick", SimClock.Tick)
                .Add("player", BuildPlayer())
                .Add("dynamics", WorldSampler.Dynamics(center, radius));

            if (includeGrid && WorldSampler.HasMap)
            {
                result.Add("localGrid", new JsonObj()
                    .Add("centeredOn", center)
                    .Add("cellSize", WorldSampler.CellSize)
                    .Add("width", localWidth)
                    .Add("height", localHeight)
                    .Add("rows", WorldSampler.LocalRows(center, localWidth, localHeight, player)));
            }

            return result;
        }

        private JsonObj BuildPlayer()
        {
            GameManager gm = GameManager.instance;
            Player player = gm != null ? gm.player : null;
            if (player == null)
            {
                // The physical fields are zeroed - there is no body to read - but the run's
                // progress tallies are GameManager state, not body state, and stay raw and
                // reportable (I5): the score at death is the score the route earned.
                return new JsonObj()
                    .Add("alive", false)
                    .Add("pos", Vector2.zero)
                    .Add("vel", Vector2.zero)
                    .Add("grounded", false)
                    .Add("facing", 1)
                    .Add("atCheckpoint", ResetController.CurrentCheckpoint)
                    .Add("fruitsCollected", gm != null ? gm.fruitsCollected : 0)
                    .Add("score", gm != null ? gm.score : 0);
            }

            return new JsonObj()
                .Add("alive", true)
                .Add("pos", player.SimPosition)
                .Add("vel", player.SimVelocity)
                .Add("aabb", player.SimHalfExtents)
                .Add("grounded", player.IsGrounded)
                .Add("wallDetected", player.IsWallDetected)
                .Add("canDoubleJump", player.CanDoubleJumpNow)
                .Add("facing", player.FacingDirection)
                .Add("atCheckpoint", ResetController.CurrentCheckpoint)
                .Add("fruitsCollected", gm != null ? gm.fruitsCollected : 0)
                .Add("score", gm != null ? gm.score : 0);
        }

        private string QuiescenceWarning()
        {
            Player player = GameManager.instance != null ? GameManager.instance.player : null;
            if (player == null) return "player is dead; observing advances the world only";
            if (!player.IsGrounded) return "player is airborne - observing moves the player too";
            if (player.SimVelocity.sqrMagnitude > 0.01f) return "player is moving - observing moves the player too";
            return null;
        }
    }
}
