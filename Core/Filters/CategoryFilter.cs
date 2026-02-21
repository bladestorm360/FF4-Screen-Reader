using FFIV_ScreenReader.Field;

namespace FFIV_ScreenReader.Core.Filters
{
    /// <summary>
    /// Filters entities by category (NPCs, Chests, Map Exits, etc.).
    /// </summary>
    public class CategoryFilter : BaseEntityFilter
    {
        public CategoryFilter() : base(defaultEnabled: true) { }

        public override string Name => "Category Filter";

        public override FilterTiming Timing => FilterTiming.OnAdd;

        /// <summary>
        /// The target category to filter by.
        /// EntityCategory.All allows all categories through.
        /// </summary>
        public EntityCategory TargetCategory { get; set; } = EntityCategory.All;

        public override bool PassesFilter(NavigableEntity entity, FilterContext context)
        {
            if (TargetCategory == EntityCategory.All)
                return true;

            return entity.Category == TargetCategory;
        }
    }
}
