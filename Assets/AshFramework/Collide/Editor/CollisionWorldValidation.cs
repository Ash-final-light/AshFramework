using System;
using System.Collections.Generic;
using AshFramework.Collide.Unity;
using UnityEngine;

namespace AshFramework.Collide.Editor
{
    /// <summary>只读配置检查。独立查找明确绑定的场景对象，不依赖运行时注册或几何缓存。</summary>
    public static class CollisionWorldValidation
    {
        public static bool CheckPlaneConsistency(CollisionWorldBehaviour world,
            List<CollisionBodyBehaviour> mismatches, out int checkedCount, out string message)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (mismatches == null)
            {
                throw new ArgumentNullException(nameof(mismatches));
            }

            mismatches.Clear();
            checkedCount = 0;
            if (world.Plane != CollisionPlane.XY && world.Plane != CollisionPlane.XZ)
            {
                message = "世界平面无效，请选择 XY 或 XZ。";
                return false;
            }

            foreach (CollisionBodyBehaviour body in Resources.FindObjectsOfTypeAll<CollisionBodyBehaviour>())
            {
                if (!body.gameObject.scene.IsValid() || !body.gameObject.scene.isLoaded || body.World != world)
                {
                    continue;
                }

                checkedCount++;
                if (body.Plane != world.Plane)
                {
                    mismatches.Add(body);
                }
            }

            if (checkedCount == 0)
            {
                message = "没有显式绑定到此世界的场景 Body，检查数量为 0。";
            }
            else
            {
                message = "已检查 " + checkedCount + " 个 Body（包含禁用对象），平面不一致：" + mismatches.Count + "。";
            }

            return mismatches.Count == 0;
        }
    }
}
