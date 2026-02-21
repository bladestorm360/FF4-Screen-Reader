using FFIV_ScreenReader.Field;
using UnityEngine;

namespace FFIV_ScreenReader.Core.Filters
{
    /// <summary>
    /// Filters entities by whether they have a valid path from the player.
    /// This is an expensive filter as it runs pathfinding for each entity.
    /// </summary>
    public class PathfindingFilter : BaseEntityFilter
    {
        public override string Name => "Pathfinding Filter";

        public override FilterTiming Timing => FilterTiming.OnCycle;

        public override bool PassesFilter(NavigableEntity entity, FilterContext context)
        {
            if (!IsEntityValid(entity))
                return false;

            if (context.PlayerController?.fieldPlayer == null)
                return false;

            Vector3 playerPos = context.PlayerController.fieldPlayer.transform.localPosition;
            Vector3 targetPos = entity.GameEntity.transform.localPosition;

            var pathInfo = FieldNavigationHelper.FindPathTo(
                playerPos,
                targetPos,
                context.MapHandle,
                context.FieldPlayer
            );

            return pathInfo.Success;
        }

        private bool IsEntityValid(NavigableEntity entity)
        {
            if (entity?.GameEntity == null)
                return false;

            try
            {
                if (entity.GameEntity.gameObject == null || !entity.GameEntity.gameObject.activeInHierarchy)
                    return false;

                if (entity.GameEntity.transform == null)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
