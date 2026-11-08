using System;
using System.Reflection;
using System.Linq.Expressions;

namespace RuinarchDebug
{
    public partial class AutoTest
    {
        private readonly struct RevisionDirection
        {
            internal readonly double X, Y, Z;
            internal RevisionDirection(double x, double y, double z) { X = x; Y = y; Z = z; }
            internal static RevisionDirection Add(RevisionDirection a, RevisionDirection b) => new RevisionDirection(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
            internal static RevisionDirection Scale(RevisionDirection a, double scale) => new RevisionDirection(a.X * scale, a.Y * scale, a.Z * scale);
            internal static double Dot(RevisionDirection a, RevisionDirection b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
            internal RevisionDirection Normalized() => Scale(this, 1 / Math.Sqrt(Dot(this, this)));
        }

        private static (bool, string) GeographicContinuity(object world)
        {
            Assembly assembly = world.GetType().Assembly;
            Type geographyType = assembly.GetType("TruePlanet.PlanetGeography", true);
            Type vectorType = assembly.GetType("TruePlanet.UnitVector", true);
            object geography = Activator.CreateInstance(geographyType, new[] { world });
            Array recorded = (Array)RevisionMember(geography, "_plates");
            var plates = new RevisionDirection[recorded.Length];
            FieldInfo x = vectorType.GetField("X"), y = vectorType.GetField("Y"), z = vectorType.GetField("Z");
            for (int i = 0; i < plates.Length; i++)
            {
                object point = recorded.GetValue(i);
                plates[i] = new RevisionDirection((double)x.GetValue(point), (double)y.GetValue(point), (double)z.GetValue(point));
            }
            ConstructorInfo vector = vectorType.GetConstructor(new[] { typeof(double), typeof(double), typeof(double) });
            MethodInfo sample = geographyType.GetMethod("Sample", RevisionMembers);
            Type sampleType = assembly.GetType("TruePlanet.TerrainSample", true);
            FieldInfo height = sampleType.GetField("Height"), mountains = sampleType.GetField("Mountains");
            double worstHeight = 0, worstMountain = 0;
            int pairs = 0;
            for (int first = 0; first < plates.Length; first++)
            for (int second = 0; second < plates.Length; second++)
            for (int third = second + 1; third < plates.Length; third++)
            {
                if (first == second || first == third) continue;
                RevisionDirection normal = RevisionDirection.Add(plates[second], RevisionDirection.Scale(plates[third], -1)).Normalized();
                RevisionDirection point = RevisionDirection.Add(plates[first], RevisionDirection.Scale(normal, -RevisionDirection.Dot(plates[first], normal)));
                if (RevisionDirection.Dot(point, point) < 1e-12) continue;
                point = point.Normalized();
                double leading = RevisionDirection.Dot(point, plates[first]), runner = RevisionDirection.Dot(point, plates[second]);
                if (leading < runner + 1e-5) continue;
                bool adjacent = true;
                for (int other = 0; other < plates.Length; other++)
                    if (other != first && other != second && other != third && RevisionDirection.Dot(point, plates[other]) > runner - 1e-5)
                        adjacent = false;
                if (!adjacent) continue;
                RevisionDirection a = RevisionDirection.Add(point, RevisionDirection.Scale(normal, -1e-9)).Normalized();
                RevisionDirection b = RevisionDirection.Add(point, RevisionDirection.Scale(normal, 1e-9)).Normalized();
                object left = sample.Invoke(geography, new[] { vector.Invoke(new object[] { a.X, a.Y, a.Z }) });
                object right = sample.Invoke(geography, new[] { vector.Invoke(new object[] { b.X, b.Y, b.Z }) });
                worstHeight = Math.Max(worstHeight, Math.Abs((double)height.GetValue(left) - (double)height.GetValue(right)));
                worstMountain = Math.Max(worstMountain, Math.Abs((double)mountains.GetValue(left) - (double)mountains.GetValue(right)));
                pairs++;
            }
            return (pairs > 0 && worstHeight < 1e-6 && worstMountain < 1e-6,
                $"plate boundaries={pairs}; height jump={worstHeight:R}; mountain jump={worstMountain:R}");
        }

        private static (bool, string) LocalFootprintOwnership(object world)
        {
            Assembly assembly = world.GetType().Assembly;
            Type geographyType = assembly.GetType("TruePlanet.PlanetGeography", true);
            object geography = Activator.CreateInstance(geographyType, new[] { world });
            object land = RevisionStatic(assembly.GetType("TruePlanet.TileLand", true), "For", world);
            Array cells = (Array)RevisionMember(land, "Cells");
            object mesh = RevisionMember(world, "Mesh");
            MethodInfo locate = mesh.GetType().GetMethod("Locate", RevisionMembers), footprint = geographyType.GetMethod("TileFootprint", RevisionMembers);
            MethodInfo direction = assembly.GetType("TruePlanet.LocalTileFootprint", true).GetMethod("Direction", RevisionMembers);
            double[] coordinates = { .01, .5, .99 };
            int samples = 0, outside = 0;
            for (int tile = 0; tile < cells.Length; tile++)
            {
                if (!(bool)RevisionMember(cells.GetValue(tile), "Eligible")) continue;
                object chart = footprint.Invoke(geography, new object[] { tile });
                foreach (double u in coordinates) foreach (double v in coordinates)
                {
                    object point = direction.Invoke(chart, new object[] { u, v });
                    if ((int)locate.Invoke(mesh, new[] { point }) != tile) outside++;
                    samples++;
                }
            }
            return (samples > 0 && outside == 0, $"native footprint samples={samples}; outside owning cell={outside}");
        }
        private static (bool, string) MountainInteriorEligibility(object world)
        {
            Assembly assembly = world.GetType().Assembly;
            Type vectorType = assembly.GetType("TruePlanet.UnitVector", true), sampleType = assembly.GetType("TruePlanet.TerrainSample", true);
            Type delegateType = typeof(Func<,>).MakeGenericType(vectorType, sampleType);
            object mesh = RevisionMember(world, "Mesh");
            MethodInfo build = assembly.GetType("TruePlanet.TileLand", true).GetMethod("Build", RevisionMembers);
            int blocked = 0, lowland = 0, dry = 0, total = 0;
            foreach (bool mountain in new[] { false, true })
            {
                var sample = Expression.MemberInit(Expression.New(sampleType),
                    Expression.Bind(sampleType.GetField("Height"), Expression.Constant(.4)),
                    Expression.Bind(sampleType.GetField("Mountains"), Expression.Constant(mountain ? .95 : .1)));
                Delegate sampler = Expression.Lambda(delegateType, sample, Expression.Parameter(vectorType)).Compile();
                object land = build.Invoke(null, new object[] { mesh, sampler, true, true });
                Array cells = (Array)RevisionMember(land, "Cells"); total = cells.Length;
                foreach (object cell in cells)
                {
                    if ((bool)RevisionMember(cell, "Eligible")) { if (mountain) blocked++; else lowland++; }
                    if (mountain && (double)RevisionMember(cell, "LandArea") > 0) dry++;
                }
            }
            return (blocked == 0 && lowland == total && dry == total,
                $"solid mountain playable={blocked}; lowland playable={lowland}/{total}; mountains remain dry land={dry}/{total}");
        }
    }
}
