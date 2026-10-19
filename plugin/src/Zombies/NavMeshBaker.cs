using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Straft4Dead.Zombies
{
    /// <summary>STRAFTAT maps ship without NavMeshData (systems-map §7), so we build one at load.
    ///
    /// Map collision is usually one merged MeshCollider with no CPU-side mesh data, which Unity's NavMeshBuilder
    /// can't read (and its GPU buffers are empty). So we sample walkable ground with physics queries into a
    /// heightfield, turn that into continuous surface meshes, and bake each connected surface on its own: baking the
    /// floor together with the block tops 8.7 m above it left the floor riddled with holes (Arena_00), while each
    /// surface alone bakes cleanly. Surfaces more than a step apart aren't connected anyway until we add off-mesh
    /// links (jumps, drops) in a later phase.</summary>
    internal static class NavMeshBaker
    {
        private const float Cell = 0.4f;          // heightfield spacing (m)
        private const float MinSurfaceArea = 4f;  // smaller pieces (ledges, sills) are dropped

        private static readonly List<NavMeshDataInstance> Instances = new List<NavMeshDataInstance>();

        /// <summary>Bakes the navmesh for one map scene. Returns the triangle count.</summary>
        public static int Bake(Scene map)
        {
            Clear();
            var settings = NavMesh.GetSettingsByID(0);
            settings.agentRadius = 0.35f;
            settings.agentHeight = 1.8f;
            settings.agentClimb = 0.45f;
            settings.agentSlope = 50f;

            int mask = EnvironmentMask;

            if (!MapBounds(map, mask, out var bounds))
                return 0;
            ClipToPlayArea(ref bounds);
            int baked = 0;
            foreach (var surface in WalkableSurfaces(bounds, mask))
            {
                var src = new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Mesh, sourceObject = surface, transform = Matrix4x4.identity, area = 0 };
                var b = surface.bounds;
                b.Expand(new Vector3(2f, 4f, 2f));
                var data = NavMeshBuilder.BuildNavMeshData(settings, new List<NavMeshBuildSource> { src }, b, Vector3.zero, Quaternion.identity);
                if (data == null)
                    continue;
                Instances.Add(NavMesh.AddNavMeshData(data));
                baked++;
            }
            var tri = NavMesh.CalculateTriangulation();
            Plugin.Logger.LogInfo($"[zombies] navmesh: {baked} surfaces baked, {tri.indices.Length / 3} triangles");
            if (Plugin.Settings.DevDrawNavMesh.Value)
                DrawDebug(tri);
            return tri.indices.Length / 3;
        }

        /// <summary>Big maps (Babbdi) would take minutes to sample: keep the bake to a square of [Zombies] MaxBakeSize
        /// around the player spawn points, which is where takes are fought.</summary>
        private static void ClipToPlayArea(ref Bounds b)
        {
            float max = Plugin.Settings.MaxBakeSize.Value;
            if (max <= 0f || (b.size.x <= max && b.size.z <= max)) return;
            var spawns = new List<Vector3>();
            foreach (var tag in new[] { "Spawnpoints", "Spawnpoints4Player" })
                try { spawns.AddRange(GameObject.FindGameObjectsWithTag(tag).Select(g => g.transform.position)); } catch (UnityException) { }
            Vector3 c = spawns.Count > 0 ? spawns.Aggregate(Vector3.zero, (a, v) => a + v) / spawns.Count
                : FirstPersonController.instance != null ? FirstPersonController.instance.transform.position : b.center;
            var size = new Vector3(Mathf.Min(b.size.x, max), b.size.y, Mathf.Min(b.size.z, max));
            var clipped = new Bounds(new Vector3(c.x, b.center.y, c.z), size);
            Plugin.Logger.LogInfo($"[zombies] map is {b.size.x:F0}x{b.size.z:F0} m: baking {size.x:F0}x{size.z:F0} m around {(spawns.Count > 0 ? spawns.Count + " spawn points" : "the player")}");
            b = clipped;
        }

        public static void Clear()
        {
            foreach (var i in Instances)
                if (i.valid) i.Remove();
            Instances.Clear();
            if (_debugMesh != null)
                Object.Destroy(_debugMesh);
        }

        /// <summary>Bounds of the map's static environment colliders (the menu scene can still be loaded alongside).</summary>
        /// <summary>Environment only: skip players, held weapons, ragdolls and UI (layer names from systems-map Q6).</summary>
        internal static int EnvironmentMask =>
            ~(LayerBit("Player") | LayerBit("Self") | LayerBit("HeldWeapon") | LayerBit("OtherHeldWeapon") | LayerBit("Body") |
              LayerBit("Members") | LayerBit("SelfBody") | LayerBit("Ragdoll") | LayerBit("UI") | LayerBit("HUD") |
              LayerBit("PauseMenuHUD") | LayerBit("Water") | LayerBit("NewGun") | LayerBit("HeadSup") | LayerBit("Interactable"));

        private static bool MapBounds(Scene map, int mask, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (var root in map.GetRootGameObjects())
            foreach (var c in root.GetComponentsInChildren<Collider>())
            {
                if (!c.enabled || c.isTrigger || (mask & (1 << c.gameObject.layer)) == 0 || (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic))
                    continue;
                if (!any) { bounds = c.bounds; any = true; } else bounds.Encapsulate(c.bounds);
            }
            return any;
        }

        /// <summary>Samples walkable ground with physics queries and returns one mesh per connected surface. Each column
        /// is raycast from the top down, restarting just under every hit (RaycastAll reports one hit per collider, and a
        /// map is usually one collider, so floors under bridges would vanish). Backface hits are on while sampling:
        /// map meshes aren't closed solids. A hit is ground when it faces up, is flat enough, and has standing room.</summary>
        private static List<Mesh> WalkableSurfaces(Bounds b, int mask)
        {
            float minUp = Mathf.Cos(50f * Mathf.Deg2Rad);
            float top = b.max.y + 2f, bottom = b.min.y - 2f;
            int nx = Mathf.CeilToInt(b.size.x / Cell) + 1, nz = Mathf.CeilToInt(b.size.z / Cell) + 1;
            var grid = new List<float>[nx, nz];
            int rays = 0, ground = 0;
            bool backfaces = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            try
            {
                for (int ix = 0; ix < nx; ix++)
                for (int iz = 0; iz < nz; iz++)
                {
                    // Off-grid offset: map edges sit on round coordinates and a ray exactly on a triangle edge can slip through.
                    var origin = new Vector3(b.min.x + ix * Cell + 0.0173f, top, b.min.z + iz * Cell + 0.0291f);
                    for (int guard = 0; guard < 24 && origin.y > bottom; guard++)
                    {
                        rays++;
                        if (!Physics.Raycast(origin, Vector3.down, out var hit, origin.y - bottom, mask, QueryTriggerInteraction.Ignore))
                            break;
                        origin = hit.point + Vector3.down * 0.02f;
                        if (hit.normal.y < minUp || hit.collider.GetComponentInParent<PlayerHealth>() != null || hit.collider.GetComponentInParent<Zombie>() != null)
                            continue; // wall, steep slope, underside of something, or an actor
                        Vector3 p = hit.point;
                        if (Physics.Raycast(p + Vector3.up * 0.05f, Vector3.up, 1.75f, mask, QueryTriggerInteraction.Ignore) ||
                            Physics.CheckCapsule(p + Vector3.up * 0.55f, p + Vector3.up * 1.6f, 0.25f, mask, QueryTriggerInteraction.Ignore))
                            continue; // under a ceiling, inside a block, or wedged against a wall
                        (grid[ix, iz] ?? (grid[ix, iz] = new List<float>())).Add(p.y);
                        ground++;
                    }
                }
            }
            finally
            {
                Physics.queriesHitBackfaces = backfaces;
            }

            // Continuous surface (a dual grid): every 2x2 block of samples within a step of each other becomes a quad
            // through the four sample points, so neighbouring quads share vertices exactly.
            var verts = new List<Vector3>();
            var tris = new List<int>();
            var index = new Dictionary<long, int>();
            int Vert(int ix, int iz, float y)
            {
                long key = ((long)ix * 100003L + iz) * 8192L + Mathf.RoundToInt((y + 1000f) * 4f);
                if (index.TryGetValue(key, out var i)) return i;
                index[key] = verts.Count;
                verts.Add(new Vector3(b.min.x + ix * Cell, y, b.min.z + iz * Cell));
                return verts.Count - 1;
            }
            bool Match(int ix, int iz, float y, out float found)
            {
                found = 0f;
                var l = grid[ix, iz];
                if (l == null) return false;
                foreach (var v in l) if (Mathf.Abs(v - y) < 0.45f) { found = v; return true; }
                return false;
            }
            for (int ix = 0; ix < nx - 1; ix++)
            for (int iz = 0; iz < nz - 1; iz++)
            {
                if (grid[ix, iz] == null) continue;
                foreach (var y0 in grid[ix, iz])
                {
                    if (!Match(ix + 1, iz, y0, out var y1) || !Match(ix, iz + 1, y0, out var y2) || !Match(ix + 1, iz + 1, y0, out var y3))
                        continue;
                    int a = Vert(ix, iz, y0), r = Vert(ix + 1, iz, y1), u = Vert(ix, iz + 1, y2), d = Vert(ix + 1, iz + 1, y3);
                    tris.Add(a); tris.Add(u); tris.Add(d); // clockwise seen from above = facing up in Unity
                    tris.Add(a); tris.Add(d); tris.Add(r);
                }
            }

            var surfaces = SplitConnected(verts, tris);
            Plugin.Logger.LogInfo($"[zombies] heightfield {b.size.x:F0}x{b.size.z:F0} m: {rays} rays, {ground} ground samples, {tris.Count / 6} quads, {surfaces.Count} surfaces");
            return surfaces;
        }

        /// <summary>Groups triangles that share vertices into separate meshes, dropping pieces under MinSurfaceArea.</summary>
        private static List<Mesh> SplitConnected(List<Vector3> verts, List<int> tris)
        {
            var parent = new int[verts.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
            for (int t = 0; t < tris.Count; t += 3)
            {
                parent[Find(tris[t + 1])] = Find(tris[t]);
                parent[Find(tris[t + 2])] = Find(tris[t]);
            }
            var groups = new Dictionary<int, List<int>>();
            for (int t = 0; t < tris.Count; t += 3)
            {
                int r = Find(tris[t]);
                if (!groups.TryGetValue(r, out var g)) groups[r] = g = new List<int>();
                g.Add(tris[t]); g.Add(tris[t + 1]); g.Add(tris[t + 2]);
            }
            var meshes = new List<Mesh>();
            foreach (var g in groups.Values)
            {
                if (g.Count / 6 * Cell * Cell < MinSurfaceArea)
                    continue;
                var map = new Dictionary<int, int>();
                var v = new List<Vector3>();
                var t = new List<int>(g.Count);
                foreach (int i in g)
                {
                    if (!map.TryGetValue(i, out var j)) { map[i] = j = v.Count; v.Add(verts[i]); }
                    t.Add(j);
                }
                var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, name = "S4D_WalkableSurface" };
                m.SetVertices(v);
                m.SetTriangles(t, 0);
                m.RecalculateBounds();
                meshes.Add(m);
            }
            return meshes;
        }

        private static GameObject _debugMesh;

        private static void DrawDebug(NavMeshTriangulation tri)
        {
            var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.vertices = System.Array.ConvertAll(tri.vertices, v => v + Vector3.up * 0.3f);
            m.triangles = tri.indices;
            _debugMesh = new GameObject("S4D_NavMeshDebug");
            _debugMesh.AddComponent<MeshFilter>().sharedMesh = m;
            var mat = new Material(Shader.Find("Sprites/Default")) { color = new Color(0.1f, 0.9f, 1f, 0.6f) }; // always in the build
            _debugMesh.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        private static int LayerBit(string name)
        {
            int l = LayerMask.NameToLayer(name);
            return l < 0 ? 0 : 1 << l;
        }
    }
}
