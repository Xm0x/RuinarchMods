using System;
using UnityEngine;

namespace TruePlanet
{
	internal static class GlobeProjection
	{
		internal static Vector3 ToUnity(UnitVector point) => new Vector3((float)point.X, (float)point.Y, (float)point.Z);

		// Rotation is a unit quaternion; radius is positive. Keep this query managed so
		// consumer boundary checks do not require a Unity scene or native quaternion call.
		internal static bool TryDirection(Ray ray, Vector3 centre, Quaternion rotation, float radius, out UnitVector direction)
		{
			direction = default;
			UnitVector origin = InverseRotate(new UnitVector((double)ray.origin.x - centre.x,
				(double)ray.origin.y - centre.y, (double)ray.origin.z - centre.z) * (1.0 / radius), rotation);
			UnitVector delta = InverseRotate(new UnitVector(ray.direction.x, ray.direction.y, ray.direction.z) * (1.0 / radius), rotation);
			double a = UnitVector.Dot(delta, delta), b = UnitVector.Dot(origin, delta), c = UnitVector.Dot(origin, origin) - 1;
			double discriminant = b * b - a * c;
			if (a <= 0 || discriminant < 0) return false;
			double root = Math.Sqrt(discriminant), t = (-b - root) / a;
			if (t < 0) t = (-b + root) / a;
			if (t < 0) return false;
			direction = (origin + delta * t).Normalized();
			return true;
		}
		private static UnitVector InverseRotate(UnitVector point, Quaternion rotation)
		{
			var q = new UnitVector(-rotation.x, -rotation.y, -rotation.z);
			return point + UnitVector.Cross(q, UnitVector.Cross(q, point) + point * rotation.w) * 2;
		}
		internal static Mesh BuildMesh(int latitudeSteps, int longitudeSteps)
		{
			int width = longitudeSteps + 1;
			var vertices = new Vector3[(latitudeSteps + 1) * width]; var uv = new Vector2[vertices.Length];
			var indices = new int[(latitudeSteps - 1) * longitudeSteps * 6]; int at = 0;
			for (int y = 0; y <= latitudeSteps; y++) for (int x = 0; x <= longitudeSteps; x++)
			{
				int i = y * width + x;
				vertices[i] = ToUnity(UnitVector.FromMap((double)x / longitudeSteps * Math.PI * 2 - Math.PI,
					(double)y / latitudeSteps * Math.PI - Math.PI / 2));
				uv[i] = new Vector2((float)x / longitudeSteps, (float)y / latitudeSteps);
			}
			for (int y = 0; y < latitudeSteps; y++) for (int x = 0; x < longitudeSteps; x++)
			{
				int a = y * width + x, b = a + 1, c = a + width, d = c + 1;
				if (y > 0) { indices[at++] = a; indices[at++] = c; indices[at++] = b; }
				if (y < latitudeSteps - 1) { indices[at++] = b; indices[at++] = c; indices[at++] = d; }
			}
			var mesh = new Mesh { name = "TruePlanet globe mesh" };
			try
			{
				mesh.vertices = vertices; mesh.normals = vertices; mesh.uv = uv;
				mesh.SetTriangles(indices, 0); mesh.RecalculateBounds(); return mesh;
			}
			catch { UnityEngine.Object.Destroy(mesh); throw; }
		}
	}
}
