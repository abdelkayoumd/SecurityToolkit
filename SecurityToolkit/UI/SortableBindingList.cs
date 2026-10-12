using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace SecurityToolkit.UI
{
    /// <summary>A BindingList that a DataGridView can sort by clicking column headers.</summary>
    internal sealed class SortableBindingList<T> : BindingList<T>
    {
        private bool _sorted;
        private PropertyDescriptor _sortProperty;
        private ListSortDirection _sortDirection;

        public SortableBindingList(IEnumerable<T> items) : base(items.ToList())
        {
        }

        protected override bool SupportsSortingCore => true;
        protected override bool IsSortedCore => _sorted;
        protected override PropertyDescriptor SortPropertyCore => _sortProperty;
        protected override ListSortDirection SortDirectionCore => _sortDirection;

        protected override void ApplySortCore(PropertyDescriptor property, ListSortDirection direction)
        {
            var ordered = direction == ListSortDirection.Ascending
                ? Items.OrderBy(i => property.GetValue(i), Comparer<object>.Create(CompareValues)).ToList()
                : Items.OrderByDescending(i => property.GetValue(i), Comparer<object>.Create(CompareValues)).ToList();

            RaiseListChangedEvents = false;
            Items.Clear();
            foreach (var item in ordered)
            {
                Items.Add(item);
            }

            RaiseListChangedEvents = true;
            _sortProperty = property;
            _sortDirection = direction;
            _sorted = true;
            OnListChanged(new ListChangedEventArgs(ListChangedType.Reset, -1));
        }

        protected override void RemoveSortCore()
        {
            _sorted = false;
            _sortProperty = null;
        }

        internal static int CompareValues(object a, object b)
        {
            if (a == null || b == null)
            {
                return a == null ? (b == null ? 0 : -1) : 1;
            }

            if (a is string sa && b is string sb)
            {
                return string.Compare(sa, sb, StringComparison.CurrentCultureIgnoreCase);
            }

            return a is IComparable ca ? ca.CompareTo(b) : string.Compare(a.ToString(), b.ToString(), StringComparison.CurrentCultureIgnoreCase);
        }
    }
}
