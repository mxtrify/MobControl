using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace MobControlUI.Core.Behaviors
{
    public static class ListBoxBehaviors
    {
        public static readonly DependencyProperty AutoScrollToEndProperty =
            DependencyProperty.RegisterAttached(
                "AutoScrollToEnd",
                typeof(bool),
                typeof(ListBoxBehaviors),
                new PropertyMetadata(false, OnAutoScrollToEndChanged));

        public static void SetAutoScrollToEnd(DependencyObject obj, bool value) =>
            obj.SetValue(AutoScrollToEndProperty, value);

        public static bool GetAutoScrollToEnd(DependencyObject obj) =>
            (bool)obj.GetValue(AutoScrollToEndProperty);

        private static void OnAutoScrollToEndChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ListBox lb) return;

            if ((bool)e.NewValue)
            {
                lb.Loaded += (_, __) => ScrollToEnd(lb);
                if (lb.Items is INotifyCollectionChanged incc)
                {
                    incc.CollectionChanged += (_, __) => ScrollToEnd(lb);
                }
            }
        }

        private static void ScrollToEnd(ListBox lb)
        {
            if (lb.Items.Count > 0)
                lb.ScrollIntoView(lb.Items[lb.Items.Count - 1]);
        }
    }
}
