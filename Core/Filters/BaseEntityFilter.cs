using FFIV_ScreenReader.Field;

namespace FFIV_ScreenReader.Core.Filters
{
    /// <summary>
    /// Base class for entity filters. Provides IsEnabled property with change detection
    /// and virtual OnEnabled/OnDisabled hooks.
    /// </summary>
    public abstract class BaseEntityFilter : IEntityFilter
    {
        private bool isEnabled;

        protected BaseEntityFilter(bool defaultEnabled = false)
        {
            isEnabled = defaultEnabled;
        }

        public bool IsEnabled
        {
            get => isEnabled;
            set
            {
                if (value != isEnabled)
                {
                    isEnabled = value;
                    if (value)
                        OnEnabled();
                    else
                        OnDisabled();
                }
            }
        }

        public abstract string Name { get; }
        public abstract FilterTiming Timing { get; }
        public abstract bool PassesFilter(NavigableEntity entity, FilterContext context);

        public virtual void OnEnabled() { }
        public virtual void OnDisabled() { }
    }
}
