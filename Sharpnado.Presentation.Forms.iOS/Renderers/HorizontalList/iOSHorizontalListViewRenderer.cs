﻿using System;
using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using CoreGraphics;
using Foundation;

using Sharpnado.Infrastructure;
using Sharpnado.Presentation.Forms.iOS.Renderers.HorizontalList;
using Sharpnado.Presentation.Forms.RenderedViews;
using UIKit;
using Xamarin.Forms;
using Xamarin.Forms.Platform.iOS;

[assembly: ExportRenderer(typeof(HorizontalListView), typeof(iOSHorizontalListViewRenderer))]
namespace Sharpnado.Presentation.Forms.iOS.Renderers.HorizontalList
{
    [Preserve]
    public partial class iOSHorizontalListViewRenderer : ViewRenderer<HorizontalListView, UICollectionView>
    {
        private IEnumerable _itemsSource;
        private UICollectionView _collectionView;

        private bool _isScrolling;
        private bool _isCurrentIndexUpdateBackfire;
        private bool _isInternalScroll;
        private bool _isMovedBackfire;

        private int _lastVisibleItemIndex = -1;

        public static void Initialize()
        {
        }

        public override void LayoutSubviews()
        {
            base.LayoutSubviews();

            double height = Bounds.Height;
            double width = Bounds.Width;

            if (_collectionView == null || height <= 0 || width <= 0)
            {
                return;
            }

            _collectionView.Frame = new CGRect(0, 0, width, height);
            SetCollectionView(_collectionView);
        }

        protected override void OnElementPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(HorizontalListView.ItemsSource):
                    UpdateItemsSource();
                    break;
                case nameof(HorizontalListView.CurrentIndex) when !_isCurrentIndexUpdateBackfire:
                    ScrollToCurrentItem();
                    break;
                case nameof(HorizontalListView.DisableScroll):
                    ProcessDisableScroll();
                    break;
            }
        }

        protected override void OnElementChanged(ElementChangedEventArgs<HorizontalListView> e)
        {
            base.OnElementChanged(e);

            if (e.OldElement != null)
            {
                if (Control != null)
                {
                    Control.DecelerationEnded -= OnStopScrolling;
                    Control.ScrollAnimationEnded -= OnStopScrolling;
                    Control.Scrolled -= OnScrolled;

                    Control.DraggingEnded -= OnDraggingEnded;
                    Control.DecelerationEnded -= OnDecelerationEnded;
                }

                if (_collectionView != null)
                {
                    _collectionView.Dispose();
                    _collectionView.DataSource?.Dispose();
                    _collectionView.CollectionViewLayout?.Dispose();
                }

                if (_itemsSource is INotifyCollectionChanged oldNotifyCollection)
                {
                    oldNotifyCollection.CollectionChanged -= OnCollectionChanged;
                }
            }

            if (e.NewElement != null)
            {
                CreateView();
            }
        }

        private void CreateView()
        {
            Control?.DataSource?.Dispose();
            Control?.CollectionViewLayout?.Dispose();
            Control?.Dispose();

            var sectionInset = new UIEdgeInsets(
                (nfloat)Element.CollectionPadding.Top,
                (nfloat)Element.CollectionPadding.Left,
                (nfloat)Element.CollectionPadding.Bottom,
                (nfloat)Element.CollectionPadding.Right);

            var layout = Element.ListLayout == HorizontalListViewLayout.Grid
                ? new UICollectionViewFlowLayout
                {
                    ScrollDirection = UICollectionViewScrollDirection.Vertical,
                    ItemSize = new CGSize(Element.ItemWidth, Element.ItemHeight),
                    MinimumInteritemSpacing = Element.ItemSpacing,
                    MinimumLineSpacing = Element.ItemSpacing,
                    SectionInset = sectionInset,
                }
                : new SnappingCollectionViewLayout(Element.SnapStyle)
                {
                    ScrollDirection = UICollectionViewScrollDirection.Horizontal,
                    ItemSize = new CGSize(Element.ItemWidth, Element.ItemHeight),
                    MinimumInteritemSpacing = Element.ItemSpacing,
                    MinimumLineSpacing = Element.ItemSpacing,
                    SectionInset = sectionInset,
                };

            // Otherwise the UICollectionView doesn't seem to take enough space
            Element.HeightRequest = Element.ItemHeight
                + Element.CollectionPadding.VerticalThickness
                + Element.Margin.VerticalThickness;

            var rect = new CGRect(0, 0, 100, Element.HeightRequest);
            _collectionView = new UICollectionView(rect, layout)
            {
                DecelerationRate =
                    Element.ScrollSpeed == ScrollSpeed.Normal
                        ? UIScrollView.DecelerationRateNormal
                        : UIScrollView.DecelerationRateFast,
                BackgroundColor = Element?.BackgroundColor.ToUIColor(),
                ShowsHorizontalScrollIndicator = false,
                ContentInset = new UIEdgeInsets(0, 0, 0, 0),
            };
        }

        private void SetCollectionView(UICollectionView collectionView)
        {
            SetNativeControl(collectionView);
            UpdateItemsSource();

            if (Element.SnapStyle == SnapStyle.Center)
            {
                Control.DraggingEnded += OnDraggingEnded;
                Control.DecelerationEnded += OnDecelerationEnded;
            }

            Control.Scrolled += OnScrolled;
            Control.ScrollAnimationEnded += OnStopScrolling;
            Control.DecelerationEnded += OnStopScrolling;

            if (Element.EnableDragAndDrop)
            {
                EnableDragAndDrop();
            }

            ScrollToCurrentItem();
            ProcessDisableScroll();
        }

        private void ScrollToCurrentItem()
        {
            if (Control == null
                || Element.CurrentIndex == -1
                || Element.CurrentIndex >= Control.NumberOfItemsInSection(0)
                || Control.NumberOfItemsInSection(0) == 0)
            {
                return;
            }

            InternalLogger.Info($"ScrollToCurrentItem( Element.CurrentIndex = {Element.CurrentIndex} )");
            _isInternalScroll = true;

            Control.LayoutIfNeeded();

            UICollectionViewScrollPosition position = UICollectionViewScrollPosition.Top;
            if (Element.ListLayout == HorizontalListViewLayout.Linear)
            {
                switch (Element.SnapStyle)
                {
                    case SnapStyle.Center:
                        position = UICollectionViewScrollPosition.CenteredHorizontally;
                        break;
                    case SnapStyle.Start:
                        position = UICollectionViewScrollPosition.Left;
                        break;
                }
            }

            Control.ScrollToItem(
                NSIndexPath.FromRowSection(Element.CurrentIndex, 0),
                position,
                false);
        }

        private void ProcessDisableScroll()
        {
            if (Control == null)
            {
                return;
            }

            Control.ScrollEnabled = !Element.DisableScroll;
        }

        private void UpdateItemsSource()
        {
            if (Control == null)
            {
                return;
            }

            InternalLogger.Info("UpdateItemsSource");
            Control.DataSource?.Dispose();
            Control.DataSource = null;

            if (_itemsSource is INotifyCollectionChanged oldNotifyCollection)
            {
                oldNotifyCollection.CollectionChanged -= OnCollectionChanged;
            }

            _itemsSource = Element.ItemsSource;
            if (_itemsSource == null)
            {
                return;
            }

            Control.DataSource = new iOSViewSource(Element);
            Control.RegisterClassForCell(typeof(iOSViewCell), nameof(iOSViewCell));

            if (_itemsSource is INotifyCollectionChanged newNotifyCollection)
            {
                newNotifyCollection.CollectionChanged += OnCollectionChanged;
            }
        }

        private void OnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (_isMovedBackfire)
            {
                return;
            }

            if (Control == null)
            {
                return;
            }

            if (Control.NumberOfItemsInSection(0) == ((IList)_itemsSource).Count)
            {
                return;
            }

            ((iOSViewSource)Control.DataSource).HandleNotifyCollectionChanged(e);

            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    var addedIndexPathes = new NSIndexPath[e.NewItems.Count];
                    for (int addedIndex = e.NewStartingIndex, index = 0;
                        index < addedIndexPathes.Length;
                        addedIndex++, index++)
                    {
                        addedIndexPathes[index] = NSIndexPath.FromRowSection(addedIndex, 0);
                    }

                    Control.InsertItems(addedIndexPathes);
                    break;
                case NotifyCollectionChangedAction.Remove:
                    var removedIndexPathes = new NSIndexPath[e.OldItems.Count];
                    for (int removedIndex = e.OldStartingIndex, index = 0;
                        index < removedIndexPathes.Length;
                        removedIndex++, index++)
                    {
                        removedIndexPathes[index] = NSIndexPath.FromRowSection(removedIndex, 0);
                    }

                    Control.DeleteItems(removedIndexPathes);
                    break;
                case NotifyCollectionChangedAction.Reset:
                    Control.ReloadData();
                    break;
                case NotifyCollectionChangedAction.Move:
                    Control.MoveItem(
                        NSIndexPath.FromRowSection(e.OldStartingIndex, 0),
                        NSIndexPath.FromRowSection(e.NewStartingIndex, 0));
                    break;
            }
        }
    }
}