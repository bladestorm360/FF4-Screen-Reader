using Il2Cpp;
using FFIV_ScreenReader.Field;

namespace FFIV_ScreenReader.Core.Filters
{
    /// <summary>
    /// Filters out ToLayer (layer transition) entities.
    /// When enabled, hides layer transitions from the navigation list.
    /// Default: disabled (ToLayer entities shown).
    /// </summary>
    public class ToLayerFilter : BaseEntityFilter
    {
        public override string Name => "Layer Transition Filter";

        public override FilterTiming Timing => FilterTiming.OnAdd;

        public override bool PassesFilter(NavigableEntity entity, FilterContext context)
        {
            if (entity is EventEntity eventEntity &&
                eventEntity.EventType == MapConstants.ObjectType.ToLayer)
            {
                return false;
            }

            return true;
        }
    }
}
