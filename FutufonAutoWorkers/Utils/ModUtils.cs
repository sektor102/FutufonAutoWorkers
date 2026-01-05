using UnityEngine;

namespace FutufonAutoWorkers.Utils
{
    internal static class ModUtils
    {
        public static Transform GetPlayerTransform()
        {
            // В MWC нет Player.main - ищем по тегу, либо вернем null
            var go = GameObject.FindWithTag("Player");
            return go != null ? go.transform : null;
        }
    }
}
