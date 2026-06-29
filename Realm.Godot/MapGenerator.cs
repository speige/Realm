using Godot;
using System;
using System.Collections.Generic;

public static class MapGenerator
{
    private static void WorldToGrid(Vector3 worldPos, int width, int depth, float spacing, out int x, out int z)
    {
        x = (int)Math.Round((worldPos.X / spacing) + (width - 1) / 2.0f);
        z = (int)Math.Round((worldPos.Z / spacing) + (depth - 1) / 2.0f);
    }

    private static Vector3 GridToWorld(int x, int z, int width, int depth, float spacing, float height)
    {
        float lx = (x - (width - 1) / 2.0f) * spacing;
        float lz = (z - (depth - 1) / 2.0f) * spacing;
        return new Vector3(lx, height, lz);
    }

    private static void CarvePathAt(Vector2I pos, int[,] baseGrid, int chokeWidth)
    {
        int radius = (int)(1.0f + (chokeWidth / 10.0f) * 3.0f);
        for (int z = -radius; z <= radius; z++)
        {
            for (int x = -radius; x <= radius; x++)
            {
                if (x * x + z * z <= radius * radius)
                {
                    int cx = pos.X + x;
                    int cz = pos.Y + z;
                    if (cx >= 0 && cx < 126 && cz >= 0 && cz < 126)
                    {
                        baseGrid[cx, cz] = 0;
                    }
                }
            }
        }
    }

    public static void GenerateMap(
        GameHost host,
        int hillsDensity,
        int terrainRoughness,
        int mountainHeight,
        int chokeWidth,
        int waterLevel,
        int treeDensity,
        int resourceAbundance,
        int decoDensity,
        string seedString
    )
    {
        if (host == null || host.GroundTerrain == null) return;

        int seed = 0;
        if (!int.TryParse(seedString, out seed))
        {
            seed = seedString.GetHashCode();
        }
        Random random = new Random(seed);

        host.ClearMapEntirely();

        int width = host.GroundTerrain.Width;
        int depth = host.GroundTerrain.Depth;
        float spacing = host.GroundTerrain.Spacing;

        int[,] baseGrid = new int[width, depth];
        double fillChance = 0.25 + (hillsDensity / 10.0) * 0.40;

        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
            {
                baseGrid[x, z] = (random.NextDouble() < fillChance) ? 1 : 0;
            }
        }

        for (int step = 0; step < 4; step++)
        {
            int[,] nextGrid = new int[width, depth];
            for (int z = 0; z < depth; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    int count = 0;
                    for (int nz = -1; nz <= 1; nz++)
                    {
                        for (int nx = -1; nx <= 1; nx++)
                        {
                            int checkX = x + nx;
                            int checkZ = z + nz;
                            if (checkX >= 0 && checkX < width && checkZ >= 0 && checkZ < depth)
                            {
                                count += baseGrid[checkX, checkZ];
                            }
                            else
                            {
                                count++;
                            }
                        }
                    }
                    if (count >= 5)
                    {
                        nextGrid[x, z] = 1;
                    }
                    else
                    {
                        nextGrid[x, z] = 0;
                    }
                }
            }
            baseGrid = nextGrid;
        }

        Vector2I[] sites = new Vector2I[]
        {
            new Vector2I(20, 20),
            new Vector2I(106, 106),
            new Vector2I(63, 63),
            new Vector2I(20, 106),
            new Vector2I(106, 20),
            new Vector2I(63, 20),
            new Vector2I(63, 106),
            new Vector2I(20, 63),
            new Vector2I(106, 63)
        };

        for (int i = 0; i < sites.Length; i++)
        {
            int clearRadius = 8;
            if (i == 0 || i == 1)
            {
                clearRadius = 15;
            }
            else if (i == 2)
            {
                clearRadius = 12;
            }

            Vector2I center = sites[i];
            for (int z = -clearRadius; z <= clearRadius; z++)
            {
                for (int x = -clearRadius; x <= clearRadius; x++)
                {
                    if (x * x + z * z <= clearRadius * clearRadius)
                    {
                        int cx = center.X + x;
                        int cz = center.Y + z;
                        if (cx >= 0 && cx < width && cz >= 0 && cz < depth)
                        {
                            baseGrid[cx, cz] = 0;
                        }
                    }
                }
            }
        }

        List<Tuple<int, int>> edges = new List<Tuple<int, int>>();
        bool[] visited = new bool[sites.Length];
        visited[0] = true;
        for (int iter = 0; iter < sites.Length - 1; iter++)
        {
            double minDist = double.MaxValue;
            int bestU = -1;
            int bestV = -1;
            for (int u = 0; u < sites.Length; u++)
            {
                if (!visited[u]) continue;
                for (int v = 0; v < sites.Length; v++)
                {
                    if (visited[v]) continue;
                    double dx = sites[u].X - sites[v].X;
                    double dy = sites[u].Y - sites[v].Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        bestU = u;
                        bestV = v;
                    }
                }
            }
            if (bestV != -1)
            {
                visited[bestV] = true;
                edges.Add(new Tuple<int, int>(bestU, bestV));
            }
        }

        bool[,] isPath = new bool[width, depth];

        foreach (var edge in edges)
        {
            Vector2I start = sites[edge.Item1];
            Vector2I end = sites[edge.Item2];

            PriorityQueue<Vector2I, float> openSet = new PriorityQueue<Vector2I, float>();
            Dictionary<Vector2I, Vector2I> cameFrom = new Dictionary<Vector2I, Vector2I>();
            Dictionary<Vector2I, float> gScore = new Dictionary<Vector2I, float>();

            gScore[start] = 0f;
            openSet.Enqueue(start, 0f);

            while (openSet.Count > 0)
            {
                Vector2I current = openSet.Dequeue();
                if (current == end) break;

                Vector2I[] neighbors = new Vector2I[]
                {
                    new Vector2I(current.X + 1, current.Y),
                    new Vector2I(current.X - 1, current.Y),
                    new Vector2I(current.X, current.Y + 1),
                    new Vector2I(current.X, current.Y - 1)
                };

                foreach (var n in neighbors)
                {
                    if (n.X < 0 || n.X >= width || n.Y < 0 || n.Y >= depth) continue;

                    float stepCost = 1.0f;
                    if (baseGrid[n.X, n.Y] == 1)
                    {
                        stepCost += 15.0f;
                    }

                    float tentativeG = gScore[current] + stepCost;
                    if (!gScore.TryGetValue(n, out float currentG) || tentativeG < currentG)
                    {
                        gScore[n] = tentativeG;
                        cameFrom[n] = current;
                        float dx = n.X - end.X;
                        float dy = n.Y - end.Y;
                        float h = (float)Math.Sqrt(dx * dx + dy * dy);
                        openSet.Enqueue(n, tentativeG + h);
                    }
                }
            }

            if (cameFrom.ContainsKey(end))
            {
                Vector2I curr = end;
                while (curr != start)
                {
                    CarvePathAt(curr, baseGrid, chokeWidth);
                    int radius = (int)(1.0f + (chokeWidth / 10.0f) * 3.0f);
                    for (int z = -radius; z <= radius; z++)
                    {
                        for (int x = -radius; x <= radius; x++)
                        {
                            if (x * x + z * z <= radius * radius)
                            {
                                int cx = curr.X + x;
                                int cz = curr.Y + z;
                                if (cx >= 0 && cx < width && cz >= 0 && cz < depth)
                                {
                                    isPath[cx, cz] = true;
                                }
                            }
                        }
                    }
                    curr = cameFrom[curr];
                }
                CarvePathAt(start, baseGrid, chokeWidth);
                isPath[start.X, start.Y] = true;
            }
        }

        for (int i = 0; i < sites.Length; i++)
        {
            Vector2I center = sites[i];
            int radius = 10;
            for (int z = -radius; z <= radius; z++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    if (x * x + z * z <= radius * radius)
                    {
                        int cx = center.X + x;
                        int cz = center.Y + z;
                        if (cx >= 0 && cx < width && cz >= 0 && cz < depth)
                        {
                            isPath[cx, cz] = true;
                        }
                    }
                }
            }
        }

        var noise = new FastNoiseLite();
        noise.NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin;
        noise.Seed = seed;
        noise.Frequency = 0.005f + (terrainRoughness / 10.0f) * 0.055f;

        float waterCutoff = 0.15f + (waterLevel / 10.0f) * 0.25f;
        float plateauHeight = 4.0f + (mountainHeight / 10.0f) * 12.0f;

        int[,] levels = new int[width, depth];

        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
            {
                float nVal = (noise.GetNoise2D(x, z) + 1.0f) / 2.0f;
                int level = 1;

                if (baseGrid[x, z] == 1)
                {
                    level = 2;
                }
                else if (nVal < waterCutoff)
                {
                    level = 0;
                }
                else
                {
                    level = 1;
                }

                levels[x, z] = level;

                float height = 0.0f;
                if (level == 0)
                {
                    height = -3.0f + (nVal / waterCutoff) * 0.5f;
                }
                else if (level == 1)
                {
                    height = 0.0f + (nVal - waterCutoff) * 0.3f;
                }
                else
                {
                    height = plateauHeight + (nVal - 0.7f) * 0.5f;
                }

                host.GroundTerrain.Heights[x, z] = height;
            }
        }

        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
            {
                int myLevel = levels[x, z];
                bool isBorder = false;
                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx;
                        int nz = z + dz;
                        if (nx >= 0 && nx < width && nz >= 0 && nz < depth)
                        {
                            if (levels[nx, nz] != myLevel)
                            {
                                isBorder = true;
                                break;
                            }
                        }
                    }
                    if (isBorder) break;
                }

                if (isBorder)
                {
                    host.GroundTerrain.Colors[x, z] = new Color(0.5f, 0.5f, 0.52f);
                }
                else
                {
                    if (myLevel == 0)
                    {
                        host.GroundTerrain.Colors[x, z] = new Color(0.85f, 0.75f, 0.5f);
                    }
                    else if (myLevel == 1)
                    {
                        host.GroundTerrain.Colors[x, z] = new Color(0.2f, 0.6f, 0.2f);
                    }
                    else
                    {
                        host.GroundTerrain.Colors[x, z] = new Color(0.95f, 0.95f, 1.0f);
                    }
                }
            }
        }

        host.GroundTerrain.UpdateMeshAndPhysics();

        Vector3 p1Base = GridToWorld(sites[0].X, sites[0].Y, width, depth, spacing, host.GroundTerrain.Heights[sites[0].X, sites[0].Y]);
        host.SpawnUnitExternal("castle", p1Base, false, 0f, 1.0f);
        host.SpawnUnitExternal("worker", p1Base + new Vector3(-6f, 0f, -6f), false, 45f, 1.0f);
        host.SpawnUnitExternal("worker", p1Base + new Vector3(-6f, 0f, 6f), false, 135f, 1.0f);
        host.SpawnUnitExternal("worker", p1Base + new Vector3(6f, 0f, -6f), false, 315f, 1.0f);
        host.SpawnUnitExternal("soldier", p1Base + new Vector3(8f, 0f, 8f), false, 225f, 1.0f);
        host.SpawnUnitExternal("soldier", p1Base + new Vector3(0f, 0f, 10f), false, 180f, 1.0f);

        Vector3 p2Base = GridToWorld(sites[1].X, sites[1].Y, width, depth, spacing, host.GroundTerrain.Heights[sites[1].X, sites[1].Y]);
        host.SpawnUnitExternal("castle", p2Base, true, 180f, 1.0f);
        host.SpawnUnitExternal("worker", p2Base + new Vector3(-6f, 0f, -6f), true, 45f, 1.0f);
        host.SpawnUnitExternal("worker", p2Base + new Vector3(-6f, 0f, 6f), true, 135f, 1.0f);
        host.SpawnUnitExternal("worker", p2Base + new Vector3(6f, 0f, -6f), true, 315f, 1.0f);
        host.SpawnUnitExternal("soldier", p2Base + new Vector3(-8f, 0f, -8f), true, 45f, 1.0f);
        host.SpawnUnitExternal("soldier", p2Base + new Vector3(0f, 0f, -10f), true, 0f, 1.0f);

        Vector3 g1 = GridToWorld(28, 20, width, depth, spacing, host.GroundTerrain.Heights[28, 20]);
        host.SpawnPropExternalWithParams("goldmine", g1, 0f, 1.0f);

        Vector3 g2 = GridToWorld(98, 106, width, depth, spacing, host.GroundTerrain.Heights[98, 106]);
        host.SpawnPropExternalWithParams("goldmine", g2, 180f, 1.0f);

        Vector3 gMid = GridToWorld(sites[2].X, sites[2].Y, width, depth, spacing, host.GroundTerrain.Heights[sites[2].X, sites[2].Y]);
        host.SpawnPropExternalWithParams("goldmine", gMid, 90f, 1.0f);

        for (int i = 3; i < sites.Length; i++)
        {
            Vector3 expPos = GridToWorld(sites[i].X, sites[i].Y, width, depth, spacing, host.GroundTerrain.Heights[sites[i].X, sites[i].Y]);
            host.SpawnPropExternalWithParams("goldmine", expPos, (float)(random.NextDouble() * 360.0), 1.0f);
        }

        float[,] pathDist = new float[width, depth];
        Queue<Vector2I> queue = new Queue<Vector2I>();
        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
            {
                if (isPath[x, z])
                {
                    pathDist[x, z] = 0f;
                    queue.Enqueue(new Vector2I(x, z));
                }
                else
                {
                    pathDist[x, z] = float.MaxValue;
                }
            }
        }

        while (queue.Count > 0)
        {
            Vector2I curr = queue.Dequeue();
            float d = pathDist[curr.X, curr.Y];
            Vector2I[] neighbors = new Vector2I[]
            {
                new Vector2I(curr.X + 1, curr.Y),
                new Vector2I(curr.X - 1, curr.Y),
                new Vector2I(curr.X, curr.Y + 1),
                new Vector2I(curr.X, curr.Y - 1)
            };
            foreach (var n in neighbors)
            {
                if (n.X >= 0 && n.X < width && n.Y >= 0 && n.Y < depth)
                {
                    if (pathDist[n.X, n.Y] > d + 1f)
                    {
                        pathDist[n.X, n.Y] = d + 1f;
                        queue.Enqueue(n);
                    }
                }
            }
        }

        float r_trees = 6.0f - (treeDensity / 10.0f) * 4.0f;
        List<Vector2> treePoints = PoissonDiscSample(random, (width - 1) * spacing, (depth - 1) * spacing, r_trees);
        foreach (var p in treePoints)
        {
            Vector3 worldPos = new Vector3(p.X - (width - 1) * spacing / 2.0f, 0f, p.Y - (depth - 1) * spacing / 2.0f);
            WorldToGrid(worldPos, width, depth, spacing, out int gx, out int gz);
            if (gx >= 0 && gx < width && gz >= 0 && gz < depth)
            {
                if (levels[gx, gz] == 1 && pathDist[gx, gz] > 4f)
                {
                    worldPos.Y = host.GroundTerrain.Heights[gx, gz];
                    float rot = (float)(random.NextDouble() * 360.0);
                    float scale = 0.8f + (float)(random.NextDouble() * 0.4);
                    host.SpawnPropExternalWithParams("tree", worldPos, rot, scale);
                }
            }
        }

        float r_deco = 15.0f - (decoDensity / 10.0f) * 10.0f;
        List<Vector2> decoPoints = PoissonDiscSample(random, (width - 1) * spacing, (depth - 1) * spacing, r_deco);
        string[] decoIds = new string[] { "rock", "pillar", "flag" };
        foreach (var p in decoPoints)
        {
            Vector3 worldPos = new Vector3(p.X - (width - 1) * spacing / 2.0f, 0f, p.Y - (depth - 1) * spacing / 2.0f);
            WorldToGrid(worldPos, width, depth, spacing, out int gx, out int gz);
            if (gx >= 0 && gx < width && gz >= 0 && gz < depth)
            {
                if (levels[gx, gz] != 0 && pathDist[gx, gz] > 2f)
                {
                    worldPos.Y = host.GroundTerrain.Heights[gx, gz];
                    float rot = (float)(random.NextDouble() * 360.0);
                    float scale = 0.7f + (float)(random.NextDouble() * 0.6);
                    string propId = decoIds[random.Next(decoIds.Length)];
                    host.SpawnPropExternalWithParams(propId, worldPos, rot, scale);
                }
            }
        }

        host.EditorHasUnsavedChanges = true;
        host.RebuildGridOverlayMeshExternal();
        MapEditorHUD.Instance?.ShowFeedbackExternal("Random map generation completed!");
    }

    private static List<Vector2> PoissonDiscSample(
        Random random,
        float width,
        float height,
        float radius,
        int maxCandidates = 30
    )
    {
        float cellSize = radius / (float)Math.Sqrt(2);
        int gridWidth = (int)Math.Ceiling(width / cellSize);
        int gridHeight = (int)Math.Ceiling(height / cellSize);
        Vector2?[,] grid = new Vector2?[gridWidth, gridHeight];

        List<Vector2> points = new List<Vector2>();
        List<Vector2> activeList = new List<Vector2>();

        Vector2 firstPoint = new Vector2(
            (float)(random.NextDouble() * width),
            (float)(random.NextDouble() * height)
        );
        points.Add(firstPoint);
        activeList.Add(firstPoint);

        int gX = (int)(firstPoint.X / cellSize);
        int gY = (int)(firstPoint.Y / cellSize);
        if (gX >= 0 && gX < gridWidth && gY >= 0 && gY < gridHeight)
        {
            grid[gX, gY] = firstPoint;
        }

        while (activeList.Count > 0)
        {
            int index = random.Next(activeList.Count);
            Vector2 point = activeList[index];
            bool found = false;

            for (int k = 0; k < maxCandidates; k++)
            {
                double angle = random.NextDouble() * 2 * Math.PI;
                double r = radius + random.NextDouble() * radius;
                Vector2 candidate = new Vector2(
                    point.X + (float)(r * Math.Cos(angle)),
                    point.Y + (float)(r * Math.Sin(angle))
                );

                if (candidate.X >= 0 && candidate.X < width && candidate.Y >= 0 && candidate.Y < height)
                {
                    int cX = (int)(candidate.X / cellSize);
                    int cY = (int)(candidate.Y / cellSize);

                    bool farEnough = true;
                    int startX = Math.Max(0, cX - 2);
                    int endX = Math.Min(gridWidth - 1, cX + 2);
                    int startY = Math.Max(0, cY - 2);
                    int endY = Math.Min(gridHeight - 1, cY + 2);

                    for (int gx = startX; gx <= endX; gx++)
                    {
                        for (int gy = startY; gy <= endY; gy++)
                        {
                            if (grid[gx, gy].HasValue)
                            {
                                float dist = (grid[gx, gy].Value - candidate).Length();
                                if (dist < radius)
                                {
                                    farEnough = false;
                                    break;
                                }
                            }
                        }
                        if (!farEnough) break;
                    }

                    if (farEnough)
                    {
                        points.Add(candidate);
                        activeList.Add(candidate);
                        grid[cX, cY] = candidate;
                        found = true;
                        break;
                    }
                }
            }

            if (!found)
            {
                activeList.RemoveAt(index);
            }
        }

        return points;
    }
}
