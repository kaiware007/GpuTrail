#ifndef GPU_TRAIL_AABB_CS_INCLUDED
#define GPU_TRAIL_AABB_CS_INCLUDED

struct AABB
{
	float3 minPos;	// AABBの最小座標
	float3 maxPos;	// AABBの最大座標
};

bool TestAABBPlane(float3 center, float3 extents, float3 normal)
{
	// Compute the projection interval radius of b onto L(t) = b.c + t * p.n
	float r = extents.x * abs(normal.x) + extents.y * abs(normal.y) + extents.z * abs(normal.z);

	// Compute distance of box center from plane
	float s = dot(normal, center);

	return -r <= s;
}
#endif // GPU_TRAIL_AABB_CS_INCLUDED
