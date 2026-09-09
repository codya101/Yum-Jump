using System.Collections.Generic;
using UnityEngine;

namespace YumJump.Agent
{
    /// <summary>
    /// Turns the live scene into the two views the agent reasons over: an ASCII occupancy grid of
    /// the static geometry and a list of dynamic objects with raw position/velocity. It classifies
    /// geometry, never behaviour - what a saw is going to do next is the client's problem
    /// (invariant I5).
    ///
    /// The grid is *authored* geometry: it is sampled once per level and re-sampled only when a
    /// different cell size is requested. Nothing invalidates it when the world changes shape, and
    /// that is deliberate - re-sampling part-way through a plan would bake a falling platform in
    /// wherever it happened to be on that tick, which is neither the authored layout nor a fact
    /// that survives the next reset. A fallen platform therefore still reads as 'p' at its
    /// starting cell; <see cref="Dynamics"/> carries its live position and is the authority for
    /// anything that moves.
    ///
    /// <para><b>The probe covers the whole cell, closed on every edge.</b> Each cell is classified
    /// by <see cref="Physics2D.OverlapArea"/> over the exact rect
    /// [origin + cell * cellSize, origin + (cell + 1) * cellSize] - not a shrunken box inside it.
    /// A probe even slightly smaller leaves an unsampled band straddling every cell boundary, and
    /// on a tilemap that band is precisely where the walls are: the level's ground is one tilemap
    /// whose CompositeCollider2D uses Outlines geometry, so the colliders are the *outline* of the
    /// filled region - thin segments, not filled cells - and the tile lattice puts every vertical
    /// face on a whole unit. The grid's boundaries are multiples of the cell size from an origin
    /// that is itself a multiple of it (Level1: origin (-100.0, -22.5) at 0.5 u), so a whole unit
    /// is always a boundary and every vertical face sat exactly in the gap. Horizontal faces did
    /// not (Level1's are at y = n - 0.21, mid-cell), which is why floors were sampled and walls
    /// were not, and why the map used to show a level with no walls at all. Measured on Level1 at
    /// 0.5 u: the shrunken probe found 1781 solid cells, the full-cell probe finds 3332.</para>
    ///
    /// <para>The cost of a closed rect is that a face lying exactly on a cell boundary registers in
    /// both neighbours, so a wall reads 0.5 u thicker than it is and a gap 0.5 u narrower. That is
    /// the same whole-cell rounding the grid already applies everywhere, and it errs the safe way -
    /// terrain that exists is never missing. It does not move any *surface*: a horizontal face
    /// inside a cell (the normal case) marks that cell and no other, so the top of a solid cell is
    /// still the real ledge and the client's fitted terrain_offset still holds. Do not shrink this
    /// probe to make hazards look tighter; <see cref="StaticHazards"/> reports their exact AABBs
    /// for that, and <see cref="Terrain"/> the exact outline of the terrain itself.</para>
    ///
    /// <para>The grid is the level's *readable* view and is what the LLM is shown. It is not the view
    /// to plan a landing against: rounding a ledge up to whole cells hangs a 0.5 u lip of '#' over
    /// every drop - a standing spot that is not there. <see cref="Terrain"/> answers that the same way
    /// <see cref="StaticHazards"/> answers it for kill volumes: exact geometry, reported alongside the
    /// grid rather than instead of it.</para>
    /// </summary>
    public static class WorldSampler
    {
        public const char Empty = '.';
        public const char Solid = '#';
        public const char OneWay = '=';
        public const char Hazard = '^';
        public const char DeadZone = 'X';
        public const char Finish = 'F';
        public const char CheckpointCell = 'C';
        public const char Start = 'S';
        public const char FruitCell = 'o';
        public const char TrampolineCell = 'T';
        public const char FanCell = 'W';
        public const char FallingPlatformCell = 'p';

        // Overlay symbols, used only in the local grid.
        public const char PlayerMark = '@';
        public const char DynamicHazard = '*';
        public const char EnemyMark = 'e';
        public const char MovingPlatform = '-';

        private static char[,] grid;          // [x, y], y = 0 at the bottom
        private static float cellSize = 0.5f;
        private static Vector2 origin;
        private static int width, height;
        private static bool sampled;

        public static bool HasMap => sampled;
        public static float CellSize => cellSize;
        public static Vector2 Origin => origin;
        public static int Width => width;
        public static int Height => height;

        public static Bounds LevelBounds { get; private set; }

        /// <summary>Legend handed to the agent alongside the map.</summary>
        public static JsonObj Legend()
        {
            return new JsonObj()
                .Add(Empty.ToString(), "empty")
                .Add(Solid.ToString(), "solid")
                .Add(OneWay.ToString(), "one_way_platform")
                .Add(Hazard.ToString(), "hazard_static")
                .Add(DeadZone.ToString(), "deadzone_kills_on_touch")
                .Add(Finish.ToString(), "finish")
                .Add(CheckpointCell.ToString(), "checkpoint")
                .Add(Start.ToString(), "start")
                .Add(FruitCell.ToString(), "collectible")
                .Add(TrampolineCell.ToString(), "trampoline")
                .Add(FanCell.ToString(), "fan_updraft")
                .Add(FallingPlatformCell.ToString(), "falling_platform_initial_position")
                .Add(PlayerMark.ToString(), "player (local grid only)")
                .Add(DynamicHazard.ToString(), "moving hazard (local grid only)")
                .Add(EnemyMark.ToString(), "enemy (local grid only)")
                .Add(MovingPlatform.ToString(), "moving platform (local grid only)");
        }

        /// <summary>Samples the whole level. Cheap enough to do on demand; cached afterwards.</summary>
        public static void SampleStaticMap(float requestedCellSize)
        {
            cellSize = requestedCellSize > 0.01f ? requestedCellSize : 0.5f;

            Bounds bounds = ComputeLevelBounds();
            LevelBounds = bounds;

            origin = new Vector2(
                Mathf.Floor(bounds.min.x / cellSize) * cellSize,
                Mathf.Floor(bounds.min.y / cellSize) * cellSize);

            width = Mathf.CeilToInt((bounds.max.x - origin.x) / cellSize) + 1;
            height = Mathf.CeilToInt((bounds.max.y - origin.y) / cellSize) + 1;

            width = Mathf.Clamp(width, 1, 4000);
            height = Mathf.Clamp(height, 1, 2000);

            grid = new char[width, height];

            var filter = new ContactFilter2D { useTriggers = true };
            filter.useLayerMask = false;
            var hits = new List<Collider2D>(16);

            // The probe is the cell, exactly: corners built from the same origin + index * cellSize
            // arithmetic the client uses for CellCenter/WorldToCell, so what the map claims about a
            // cell is what a query of that cell's rect actually found. Anything smaller leaves a
            // band at every cell boundary that no query covers, and tilemap walls live exactly
            // there - see the class docs.
            for (int x = 0; x < width; x++)
            {
                float minX = origin.x + x * cellSize;
                float maxX = origin.x + (x + 1) * cellSize;
                for (int y = 0; y < height; y++)
                {
                    float minY = origin.y + y * cellSize;
                    hits.Clear();
                    Physics2D.OverlapArea(new Vector2(minX, minY), new Vector2(maxX, minY + cellSize),
                                          filter, hits);
                    grid[x, y] = Classify(hits);
                }
            }

            sampled = true;
        }

        public static Vector2 CellCenter(int x, int y) =>
            new Vector2(origin.x + (x + 0.5f) * cellSize, origin.y + (y + 0.5f) * cellSize);

        public static Vector2Int WorldToCell(Vector2 world) => new Vector2Int(
            Mathf.FloorToInt((world.x - origin.x) / cellSize),
            Mathf.FloorToInt((world.y - origin.y) / cellSize));

        /// <summary>Rows of the full static map, top row first.</summary>
        public static List<string> Rows()
        {
            var rows = new List<string>(height);
            var sb = new System.Text.StringBuilder(width);
            for (int y = height - 1; y >= 0; y--)
            {
                sb.Length = 0;
                for (int x = 0; x < width; x++) sb.Append(grid[x, y]);
                rows.Add(sb.ToString());
            }
            return rows;
        }

        /// <summary>
        /// A window of the map around a world position, with live dynamic objects and the player
        /// stamped on top. Top row first, like the full map.
        /// </summary>
        public static List<string> LocalRows(Vector2 center, int cellsWide, int cellsHigh, Player player)
        {
            if (!sampled) SampleStaticMap(cellSize);

            Vector2Int c = WorldToCell(center);
            int x0 = c.x - cellsWide / 2;
            int y0 = c.y - cellsHigh / 2;

            char[,] window = new char[cellsWide, cellsHigh];
            for (int x = 0; x < cellsWide; x++)
            {
                for (int y = 0; y < cellsHigh; y++)
                {
                    int gx = x0 + x, gy = y0 + y;
                    window[x, y] = InBounds(gx, gy) ? grid[gx, gy] : Solid;   // outside the map reads as wall
                }
            }

            foreach (SimBehaviour b in SimRegistry.All)
            {
                if (b == null || !b.isActiveAndEnabled) continue;
                char mark;
                switch (b.Kind)
                {
                    case SimKind.Hazard: mark = DynamicHazard; break;
                    case SimKind.Enemy: mark = EnemyMark; break;
                    case SimKind.MovingPlatform: mark = MovingPlatform; break;
                    case SimKind.Collectible: mark = FruitCell; break;
                    default: continue;
                }

                Vector2 half = b.SimHalfExtents;
                Vector2 pos = b.SimPosition;
                Vector2Int min = WorldToCell(pos - half);
                Vector2Int max = WorldToCell(pos + half);
                for (int gx = min.x; gx <= max.x; gx++)
                {
                    for (int gy = min.y; gy <= max.y; gy++)
                    {
                        int wx = gx - x0, wy = gy - y0;
                        if (wx < 0 || wy < 0 || wx >= cellsWide || wy >= cellsHigh) continue;
                        window[wx, wy] = mark;
                    }
                }
            }

            if (player != null)
            {
                Vector2Int p = WorldToCell(player.SimPosition);
                int px = p.x - x0, py = p.y - y0;
                if (px >= 0 && py >= 0 && px < cellsWide && py < cellsHigh) window[px, py] = PlayerMark;
            }

            var rows = new List<string>(cellsHigh);
            var sb = new System.Text.StringBuilder(cellsWide);
            for (int y = cellsHigh - 1; y >= 0; y--)
            {
                sb.Length = 0;
                for (int x = 0; x < cellsWide; x++) sb.Append(window[x, y]);
                rows.Add(sb.ToString());
            }
            return rows;
        }

        /// <summary>Cell coordinates of the level's finish, or null if the level has none.</summary>
        public static Vector2Int? FinishCell()
        {
            FinishPoint finish = Object.FindFirstObjectByType<FinishPoint>();
            if (finish == null) return null;
            return WorldToCell(finish.transform.position);
        }

        /// <summary>
        /// Start, checkpoint and finish positions, reported separately from the grid. A marker
        /// sitting in a solid cell is invisible on the map (geometry outranks it), and the agent
        /// still needs to know where it is.
        /// </summary>
        public static List<object> Markers()
        {
            var markers = new List<object>();

            foreach (StartPoint start in Object.FindObjectsByType<StartPoint>(FindObjectsSortMode.None))
                markers.Add(Marker("start", start.SimId, start.transform.position));

            foreach (Checkpoint checkpoint in Object.FindObjectsByType<Checkpoint>(FindObjectsSortMode.None))
                markers.Add(Marker("checkpoint", checkpoint.SimId, checkpoint.transform.position));

            foreach (FinishPoint finish in Object.FindObjectsByType<FinishPoint>(FindObjectsSortMode.None))
                markers.Add(Marker("finish", finish.SimId, finish.transform.position));

            return markers;
        }

        /// <summary>
        /// Exact boxes for the <b>static</b> kill volumes (spikes, dead zones). The grid rounds a
        /// hazard up to whole cells, which is fine to look at and far too pessimistic to plan
        /// against - a jump that really does clear the spikes would read as a death.
        ///
        /// <para><b>Anything that moves is excluded, and that exclusion is the point.</b> A
        /// <see cref="Trap_Saw"/> carries a <see cref="DamageTrigger"/> like a spike does, so
        /// collecting triggers alone reported every patrolling saw as permanent geometry sitting
        /// wherever it happened to be on the tick the map was sampled - a kill box with the wrong
        /// lifetime *and* an arbitrary position, which a client reads as a stretch of corridor that
        /// is lethal for ever. Moving hazards are already reported properly by
        /// <see cref="Dynamics"/>, once per tick, with position, velocity and extents; the map's
        /// job is the part of the level that is still true on the next tick.</para>
        ///
        /// <para>Which collider is "static" is not decided here: <see cref="ClassifyCollider"/> is
        /// the single place that says what belongs to the map rather than to the dynamics list, and
        /// this asks it. A box is reported exactly when that classification is a kill volume, so
        /// nothing can be lethal on the grid and absent from the boxes. The two still differ in
        /// *extent*, and only in the pessimistic direction: the grid rounds each volume up to whole
        /// cells, and a physics query counts a collider within its contact tolerance (0.01 u) of a
        /// cell, so a handful of Level1's '^'/'X' cells sit just outside the exact box that put
        /// them there. Plan against the boxes; read the grid.</para>
        /// </summary>
        public static List<object> StaticHazards()
        {
            var hazards = new List<object>();

            foreach (DamageTrigger trigger in Object.FindObjectsByType<DamageTrigger>(FindObjectsSortMode.None))
                AddHazardBox(hazards, "hazard", trigger.gameObject);

            foreach (DeadZone zone in Object.FindObjectsByType<DeadZone>(FindObjectsSortMode.None))
                AddHazardBox(hazards, "deadzone", zone.gameObject);

            foreach (SoftRespawnZone zone in Object.FindObjectsByType<SoftRespawnZone>(FindObjectsSortMode.None))
                AddHazardBox(hazards, "deadzone", zone.gameObject);

            return hazards;
        }

        /// <summary>
        /// The exact collision geometry of the static terrain, in world units - what
        /// <see cref="StaticHazards"/> is for kill volumes. The grid rounds terrain up to whole
        /// cells, and for terrain that rounding is not merely coarse, it is *wrong in the unsafe
        /// direction*: a vertical face lying on a cell boundary (on this tilemap every one of them
        /// does, because the tile lattice puts them on whole units) marks the cell on both sides, so
        /// every ledge end grows a 0.5 u lip of '#' hanging over the drop and every filled block
        /// reads as a hollow rectangle. A client that plans a landing on that lip falls.
        ///
        /// <para><b>The format is closed polygon paths.</b> Each entry is one terrain collider:
        /// <c>{"id", "kind": "solid" | "one_way" | "falling", "paths": [[[x,y], ...], ...]}</c>. A
        /// path is a closed loop - the last point joins the first - and the winding is normalised so
        /// that <b>the solid side is always to the left of each directed edge</b> (counter-clockwise
        /// outlines, clockwise holes). That is what lets the client tell a floor from a ceiling
        /// without guessing: walking along an edge with the solid on the left, a horizontal edge
        /// running -x has solid below it (a floor) and one running +x has solid above it (a ceiling).
        /// Paths, not classified floors/walls, because paths are what the collider actually is
        /// (invariant I5); the classification is one line of client code and belongs there.</para>
        ///
        /// <para>A <see cref="CompositeCollider2D"/> reports its real outline via
        /// <c>pathCount</c>/<c>GetPath</c> - for a tilemap that is the exact boundary of the filled
        /// region, which is also exactly what the physics collides against. Every other terrain
        /// collider is reported as its bounding box, as a four-point path: correct for the
        /// axis-aligned boxes this game uses, and an over-estimate for anything rotated or round,
        /// which is the same direction the grid already errs in.</para>
        ///
        /// <para>Bounded: an outline over <see cref="MaxTerrainPoints"/> points is not reported at
        /// all, and the response carries <c>terrainOmitted</c> saying why. Reporting a subset would
        /// be worse than reporting nothing - missing terrain reads as open air, and a client would
        /// plan straight through it - whereas no terrain at all just puts the client back on the
        /// grid, which is where it started. Level1's whole outline is 148 points in 18 loops
        /// (1.8 KB on the wire), so this is a guard rail rather than a working limit.</para>
        /// </summary>
        public static List<object> Terrain()
        {
            var list = new List<object>();

            var colliders = new List<Collider2D>(
                Object.FindObjectsByType<Collider2D>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
            // Stable order, never instance id: the same level must serialise the same way twice.
            colliders.Sort(CompareTerrainColliders);

            int points = 0;
            foreach (Collider2D col in colliders)
            {
                if (col == null || !col.enabled) continue;
                // A collider merged into a composite has no geometry of its own; the composite
                // carries it, and reporting both would double every face.
                if (col.composite != null) continue;

                char symbol;
                int rank;
                if (!ClassifyCollider(col, out symbol, out rank)) continue;

                string kind;
                if (symbol == Solid) kind = "solid";
                else if (symbol == OneWay) kind = "one_way";
                else if (symbol == FallingPlatformCell) kind = "falling";
                else continue;   // hazards, markers, pickups - not something to stand on

                var paths = new List<object>();
                var composite = col as CompositeCollider2D;
                if (composite != null) points += AddCompositePaths(paths, composite);
                else points += AddBoxPath(paths, col.bounds);

                if (paths.Count == 0) continue;
                if (points > MaxTerrainPoints) return null;

                list.Add(new JsonObj()
                    .Add("id", col.gameObject.name + "#" + list.Count)
                    .Add("kind", kind)
                    .Add("paths", paths));
            }

            return list;
        }

        /// <summary>Point budget for <see cref="Terrain"/>; over it, nothing is reported.</summary>
        public const int MaxTerrainPoints = 12000;

        private static int CompareTerrainColliders(Collider2D a, Collider2D b)
        {
            int byName = string.CompareOrdinal(a.gameObject.name, b.gameObject.name);
            if (byName != 0) return byName;
            Bounds ba = a.bounds, bb = b.bounds;
            int byX = ba.center.x.CompareTo(bb.center.x);
            return byX != 0 ? byX : ba.center.y.CompareTo(bb.center.y);
        }

        /// <summary>
        /// The composite's own outline, in world space, wound so the solid side is on the left.
        /// </summary>
        private static int AddCompositePaths(List<object> into, CompositeCollider2D composite)
        {
            Transform t = composite.transform;
            var loops = new List<List<Vector2>>(composite.pathCount);
            int points = 0;

            // Unity does not promise which way round it hands out outlines, and getting it backwards
            // would turn every floor into a ceiling. The total signed area settles it without a
            // guess: for a valid outline set - outer loops one way, holes the other - its sign is the
            // sign of the outer loops, because the outers enclose the holes. Positive is
            // counter-clockwise, which is the convention the wire format documents.
            double signedArea = 0.0;

            for (int i = 0; i < composite.pathCount; i++)
            {
                int count = composite.GetPathPointCount(i);
                if (count < 3) continue;

                var local = new Vector2[count];
                composite.GetPath(i, local);

                var world = new List<Vector2>(count);
                for (int p = 0; p < count; p++) world.Add(t.TransformPoint(local[p]));

                signedArea += SignedArea(world);
                loops.Add(world);
                points += count;
            }

            bool flip = signedArea < 0.0;
            foreach (List<Vector2> loop in loops)
            {
                if (flip) loop.Reverse();
                var path = new List<object>(loop.Count);
                foreach (Vector2 p in loop) path.Add(p);
                into.Add(path);
            }
            return points;
        }

        /// <summary>An AABB as a four-point counter-clockwise path.</summary>
        private static int AddBoxPath(List<object> into, Bounds b)
        {
            into.Add(new List<object>
            {
                new Vector2(b.min.x, b.min.y),
                new Vector2(b.max.x, b.min.y),
                new Vector2(b.max.x, b.max.y),
                new Vector2(b.min.x, b.max.y),
            });
            return 4;
        }

        /// <summary>Twice the signed area of a closed loop; positive means counter-clockwise.</summary>
        private static double SignedArea(List<Vector2> loop)
        {
            double sum = 0.0;
            for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
                sum += (double)loop[j].x * loop[i].y - (double)loop[i].x * loop[j].y;
            return sum;
        }

        private static void AddHazardBox(List<object> into, string kind, GameObject go)
        {
            Collider2D[] colliders = go.GetComponentsInChildren<Collider2D>();
            foreach (Collider2D col in colliders)
            {
                if (col == null || !col.enabled) continue;
                if (!IsStaticKillVolume(col)) continue;
                Bounds b = col.bounds;
                into.Add(new JsonObj()
                    .Add("id", go.name + "#" + into.Count)
                    .Add("kind", kind)
                    .Add("pos", (Vector2)b.center)
                    .Add("aabb", (Vector2)b.extents));
            }
        }

        /// <summary>
        /// Does this collider kill on contact <i>and</i> stay where it is? The second half is what
        /// <see cref="StaticHazards"/> needs and a component lookup alone cannot answer: a saw's
        /// <see cref="DamageTrigger"/> is indistinguishable from a spike's.
        ///
        /// <para>The judgement is <see cref="ClassifyCollider"/>'s, unchanged - it already refuses
        /// every collider whose <see cref="SimBehaviour.Kind"/> is dynamic, because a moving object
        /// baked into the map is exactly as wrong on the grid as it is in a box. Reusing it means
        /// the two views cannot drift apart; a second, hand-written "is it a saw" test is how they
        /// would.</para>
        /// </summary>
        private static bool IsStaticKillVolume(Collider2D col)
        {
            char symbol;
            int rank;
            if (!ClassifyCollider(col, out symbol, out rank)) return false;   // it moves, or it is nothing
            return symbol == Hazard || symbol == DeadZone;
        }

        private static object Marker(string type, string id, Vector2 position)
        {
            Vector2Int cell = WorldToCell(position);
            return new JsonObj()
                .Add("type", type)
                .Add("id", id)
                .Add("pos", position)
                .Add("cell", new List<object> { cell.x, cell.y });
        }

        /// <summary>Raw description of every live dynamic object (invariant I5: no interpretation).</summary>
        public static List<object> Dynamics(Vector2 around, float radius)
        {
            var list = new List<object>();
            foreach (SimBehaviour b in SimRegistry.All)
            {
                if (b == null || !b.isActiveAndEnabled) continue;
                if (b.Kind == SimKind.None || b.Kind == SimKind.Player) continue;

                Vector2 pos = b.SimPosition;
                if (radius > 0f && Vector2.Distance(pos, around) > radius) continue;

                var fields = new JsonObj()
                    .Add("objId", b.SimId)
                    .Add("kind", KindName(b.Kind))
                    .Add("pos", pos)
                    .Add("vel", b.SimVelocity)
                    .Add("aabb", b.SimHalfExtents);

                var extra = new Dictionary<string, object>();
                b.DescribeTo(extra);
                foreach (var kv in extra) fields.Add(kv.Key, kv.Value);

                list.Add(fields);
            }
            return list;
        }

        /// <summary>Positions only - the compact per-tick payload used by observe traces.</summary>
        public static List<object> DynamicsPositions()
        {
            var list = new List<object>();
            foreach (SimBehaviour b in SimRegistry.All)
            {
                if (b == null || !b.isActiveAndEnabled) continue;
                if (b.Kind == SimKind.None || b.Kind == SimKind.Player) continue;
                list.Add(new JsonObj().Add("objId", b.SimId).Add("pos", b.SimPosition));
            }
            return list;
        }

        public static string KindName(SimKind kind)
        {
            switch (kind)
            {
                case SimKind.MovingPlatform: return "moving_platform";
                case SimKind.Hazard: return "hazard";
                case SimKind.Enemy: return "enemy";
                case SimKind.Collectible: return "collectible";
                case SimKind.Trigger: return "trigger";
                case SimKind.Player: return "player";
                default: return "unknown";
            }
        }

        // ------------------------------------------------------------------ internals

        private static bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < width && y < height;

        private static char Classify(List<Collider2D> hits)
        {
            char best = Empty;
            int bestRank = 0;

            foreach (Collider2D col in hits)
            {
                if (col == null) continue;

                char symbol;
                int rank;
                if (!ClassifyCollider(col, out symbol, out rank)) continue;

                if (rank > bestRank)
                {
                    bestRank = rank;
                    best = symbol;
                }
            }
            return best;
        }

        /// <summary>
        /// What one collider is, as far as the *map* is concerned - and therefore also what it is
        /// as far as <see cref="StaticHazards"/> and <see cref="Terrain"/> are concerned, both of
        /// which ask this rather than repeating the judgement. False means "not static geometry":
        /// either it belongs to something that moves (it is in <see cref="Dynamics"/> instead) or
        /// it is nothing the client can act on.
        /// </summary>
        private static bool ClassifyCollider(Collider2D col, out char symbol, out int rank)
        {
            symbol = Empty;
            rank = 0;
            GameObject go = col.gameObject;

            // Dynamic objects are reported in the dynamics list, not baked into the static map -
            // except falling platforms, which sit still until touched and are useful as terrain.
            SimBehaviour sim = col.GetComponentInParent<SimBehaviour>();
            if (sim != null)
            {
                if (sim is FallingPlatform) { symbol = FallingPlatformCell; rank = 80; return true; }
                if (sim is Fruit) { symbol = FruitCell; rank = 10; return true; }
                if (sim.Kind == SimKind.Hazard || sim.Kind == SimKind.Enemy ||
                    sim.Kind == SimKind.MovingPlatform || sim.Kind == SimKind.Player) return false;
            }

            // Ranking matters: what kills you outranks what you stand on, and what you stand on
            // outranks decoration. Flag triggers in particular reach down into the floor, and a
            // map that reported that floor as a flag would have the client walking through it.
            if (go.GetComponentInParent<DamageTrigger>() != null) { symbol = Hazard; rank = 100; return true; }
            if (go.GetComponentInParent<DeadZone>() != null || go.GetComponentInParent<SoftRespawnZone>() != null)
            {
                symbol = DeadZone;
                rank = 95;
                return true;
            }

            if (!col.isTrigger)
            {
                bool oneWay = col.GetComponent<PlatformEffector2D>() != null || col.usedByEffector;
                symbol = oneWay ? OneWay : Solid;
                rank = oneWay ? 88 : 90;
                return true;
            }

            if (go.GetComponentInParent<FinishPoint>() != null) { symbol = Finish; rank = 70; return true; }
            if (go.GetComponentInParent<Checkpoint>() != null) { symbol = CheckpointCell; rank = 68; return true; }
            if (go.GetComponentInParent<StartPoint>() != null) { symbol = Start; rank = 66; return true; }
            if (go.GetComponentInParent<Trampoline>() != null) { symbol = TrampolineCell; rank = 64; return true; }
            if (go.GetComponentInParent<Fan>() != null) { symbol = FanCell; rank = 62; return true; }

            return false;
        }

        private static Bounds ComputeLevelBounds()
        {
            bool any = false;
            Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);

            Collider2D[] colliders = Object.FindObjectsByType<Collider2D>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (Collider2D col in colliders)
            {
                if (col == null) continue;
                // Kill volumes are often enormous; they must not inflate the sampled area.
                if (col.GetComponentInParent<DeadZone>() != null) continue;
                if (col.GetComponentInParent<SoftRespawnZone>() != null) continue;
                if (col.isTrigger && col.GetComponentInParent<SimBehaviour>() == null &&
                    col.GetComponentInParent<FinishPoint>() == null &&
                    col.GetComponentInParent<Checkpoint>() == null &&
                    col.GetComponentInParent<StartPoint>() == null) continue;

                if (!any) { bounds = col.bounds; any = true; }
                else bounds.Encapsulate(col.bounds);
            }

            if (!any) bounds = new Bounds(Vector3.zero, new Vector3(64f, 32f, 0f));

            bounds.Expand(new Vector3(4f, 6f, 0f));
            return bounds;
        }
    }
}
