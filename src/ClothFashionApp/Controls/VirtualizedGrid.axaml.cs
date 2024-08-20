using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Threading;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;

namespace ClothFashionApp.Controls;

public class VirtualizedGrid : TemplatedControl
{
    bool _isTemplateApplied;

    bool _supressCache;
    DateTimeOffset _updateTime = DateTimeOffset.MinValue;
    Task? _updateTask = null;

    ScrollViewer PART_ScrollViewer = null!;
    ScrollViewer PART_ClipScrollViewer = null!;
    Border PART_InnerCanvas = null!;
    Canvas PART_ItemsCanvas = null!;

    List<List<Control>> _renderedControls = new();

    public static readonly StyledProperty<double> ItemHeightProperty =
        AvaloniaProperty.Register<VirtualizedGrid, double>(nameof(ItemHeight), 64);

    public static readonly StyledProperty<double> ItemWidthProperty =
        AvaloniaProperty.Register<VirtualizedGrid, double>(nameof(ItemWidth), 64);

    public static readonly StyledProperty<IList> ItemsProperty =
        AvaloniaProperty.Register<VirtualizedGrid, IList>(nameof(Items));

    public static readonly StyledProperty<int> MaxItemsInRowProperty =
        AvaloniaProperty.Register<VirtualizedGrid, int>(nameof(MaxItemsInRow));

    public static readonly StyledProperty<IDataTemplate> ItemTemplateProperty =
        AvaloniaProperty.Register<VirtualizedGrid, IDataTemplate>(nameof(ItemTemplate));

    public static readonly StyledProperty<bool> DisableSmoothScrollingProperty =
        AvaloniaProperty.Register<VirtualizedGrid, bool>(nameof(DisableSmoothScrolling));

    public static readonly StyledProperty<int> MaxItemsCacheProperty =
        AvaloniaProperty.Register<VirtualizedGrid, int>(nameof(MaxItemsCache));

    public static readonly StyledProperty<int> RefreshDelayProperty =
        AvaloniaProperty.Register<VirtualizedGrid, int>(nameof(RefreshDelay));

    public double ItemHeight
    {
        get => GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    public double ItemWidth
    {
        get => GetValue(ItemWidthProperty);
        set => SetValue(ItemWidthProperty, value);
    }

    public IList Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public int MaxItemsInRow
    {
        get => GetValue(MaxItemsInRowProperty);
        set => SetValue(MaxItemsInRowProperty, value);
    }

    public IDataTemplate ItemTemplate
    {
        get => GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    public bool DisableSmoothScrolling
    {
        get => GetValue(DisableSmoothScrollingProperty);
        set => SetValue(DisableSmoothScrollingProperty, value);
    }

    public int MaxItemsCache
    {
        get => GetValue(MaxItemsCacheProperty);
        set => SetValue(MaxItemsCacheProperty, value);
    }

    public int RefreshDelay
    {
        get => GetValue(RefreshDelayProperty);
        set => SetValue(RefreshDelayProperty, value);
    }

    // DEBUG

    public static readonly StyledProperty<int> RenderedControlsNumberProperty =
        AvaloniaProperty.Register<VirtualizedGrid, int>(nameof(RenderedControlsNumber),
            defaultBindingMode: BindingMode.OneWayToSource);

    public int RenderedControlsNumber
    {
        get => GetValue(RenderedControlsNumberProperty);
        set => SetValue(RenderedControlsNumberProperty, value);
    }

    // DEBUG

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        PART_ScrollViewer = e.NameScope.Find<ScrollViewer>(nameof(PART_ScrollViewer)) ??
            throw new NullReferenceException(nameof(PART_ScrollViewer));
        PART_ClipScrollViewer = e.NameScope.Find<ScrollViewer>(nameof(PART_ClipScrollViewer)) ??
            throw new NullReferenceException(nameof(PART_ClipScrollViewer));
        PART_InnerCanvas = e.NameScope.Find<Border>(nameof(PART_InnerCanvas)) ??
            throw new NullReferenceException(nameof(PART_InnerCanvas));
        PART_ItemsCanvas = e.NameScope.Find<Canvas>(nameof(PART_ItemsCanvas)) ??
            throw new NullReferenceException(nameof(PART_ItemsCanvas));

        PART_ScrollViewer.ScrollChanged += ScrollChanged;
        LayoutUpdated += (s, e) => ScheduleOrPostponeUpdate();

        _isTemplateApplied = true;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property.Name == nameof(Items))
        {
            UpdateState();

            if (Items is not null)
            {
                if (Items is INotifyCollectionChanged observableCollection)
                {
                    observableCollection.CollectionChanged += (s, e) =>
                    {
                        UpdateState();
                    };
                }
            }
        }

        if (change.Property.Name == nameof(MaxItemsInRow) ||
            change.Property.Name == nameof(ItemHeight) ||
            change.Property.Name == nameof(ItemWidth))
        {
            _supressCache = true;
            UpdateItemsDimensions();
            UpdateState();
            _supressCache = false;
        }
    }

    private void ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (!DisableSmoothScrolling)
        {
            double xOffset = PART_ScrollViewer.Offset.X % ItemWidth;
            double yOffset = PART_ScrollViewer.Offset.Y % ItemHeight;

            PART_ClipScrollViewer.Offset = PART_ClipScrollViewer.Offset
                .WithX(xOffset)
                .WithY(yOffset);
        }

        ClearViewportDataContext();
    }

    private void ScheduleOrPostponeUpdate()
    {
        if (RefreshDelay <= 0)
        {
            UpdateState();
            return;
        }

        int refreshDelay = RefreshDelay;

        if (_updateTask is null)
        {
            // Schedule
            _updateTime = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(refreshDelay);
            _updateTask = Task.Run(async () =>
            {
                bool stop = false;
                while (!stop)
                {
                    await Task.Delay(Math.Max(refreshDelay / 10, 1));
                    if (DateTimeOffset.UtcNow > _updateTime)
                    {
                        Dispatcher.UIThread.Invoke(() =>
                        {
                            UpdateState();
                            _updateTask = null;
                            stop = true;
                        }, DispatcherPriority.Background);
                    }
                }
            });
        }
        else
        {
            // Postpone
            _updateTime = DateTimeOffset.UtcNow.AddMilliseconds(refreshDelay);
        }
    }

    /// <summary>
    /// Updates rendered items, theirs data context and scroll bars to reflect current state.
    /// </summary>
    private void UpdateState()
    {
        if (_isTemplateApplied)
        {
            UpdateItemsCanvas();
            UpdateScrollViewerBoundaries();
            UpdatViewportDataContext();

            RenderedControlsNumber = _renderedControls.Count * _renderedControls.FirstOrDefault()?.Count ?? 0;
        }
    }

    /// <summary>
    /// Ensures that number of rendered controls and canvas dimensions are matching the current state.
    /// </summary>
    private void UpdateItemsCanvas()
    {
        UpdateColumns();
        UpdateRows();
        UpdateItemsCanvasDimensions();
    }

    private void UpdateColumns()
    {
        int currentColumnsNumber = GetNumberOfRenderedItemsHorizontally();
        int expectedColumnsNumber = ResolveMaxHorizontalVisibleItems();

        if (currentColumnsNumber < expectedColumnsNumber)
        {
            AddColumns(expectedColumnsNumber - currentColumnsNumber);
        }

        if (currentColumnsNumber > expectedColumnsNumber)
        {
            int numberOfColumnsToRemove = currentColumnsNumber - expectedColumnsNumber;

            if (expectedColumnsNumber > GetMaxItemsInCurrentViewportX() && !_supressCache)
            {
                int deltaRemoveColumn = MaxItemsCache / ResolveMaxVerticalVisibleItems();
                numberOfColumnsToRemove -= deltaRemoveColumn;
            }

            RemoveColumns(numberOfColumnsToRemove);
        }
    }

    private void UpdateRows()
    {
        int currentRowsNumber = GetNumberOfRenderedItemsVertically();
        int expectedRowsNumber = ResolveMaxVerticalVisibleItems();

        if (currentRowsNumber < expectedRowsNumber)
        {
            AddRows(expectedRowsNumber - currentRowsNumber);
        }

        if (currentRowsNumber > expectedRowsNumber)
        {
            int numberOfRowToRemove = currentRowsNumber - expectedRowsNumber;

            if (expectedRowsNumber > GetMaxItemsInCurrentViewportY() && !_supressCache)
            {
                int deltaRemoveRows = (MaxItemsCache - expectedRowsNumber * (GetNumberOfRenderedItemsHorizontally() - ResolveMaxHorizontalVisibleItems())) / GetNumberOfRenderedItemsHorizontally();
                numberOfRowToRemove -= deltaRemoveRows;
            }

            RemoveRows(numberOfRowToRemove);
        }
    }

    /// <summary>
    /// Updates width and height of all rendered items.
    /// </summary>
    private void UpdateItemsDimensions()
    {
        for (int y = 0; y < _renderedControls.Count; y++)
        {
            for (int x = 0; x < _renderedControls[y].Count; x++)
            {
                Control item = _renderedControls[y][x];
                item.SetValue(HeightProperty, ItemHeight);
                item.SetValue(WidthProperty, ItemWidth);
                item.SetValue(Canvas.LeftProperty, x * ItemWidth);
                item.SetValue(Canvas.TopProperty, y * ItemHeight);
            }
        }
    }

    private void UpdateItemsCanvasDimensions()
    {
        PART_ItemsCanvas.Width = GetNumberOfRenderedItemsHorizontally() * ItemWidth;
        PART_ItemsCanvas.Height = GetNumberOfRenderedItemsVertically() * ItemHeight;
    }

    private void AddColumns(int numberToAdd)
    {
        List<Control> elementsToAdd = new();

        for (int y = 0; y < _renderedControls.Count; y++)
        {
            int currentRowNewIndex = _renderedControls[y].Count;
            for (int x = currentRowNewIndex; x < currentRowNewIndex + numberToAdd; x++)
            {
                Control newItem = CreateItem(x, y);
                _renderedControls[y].Add(newItem);
                elementsToAdd.Add(newItem);
            }
        }

        PART_ItemsCanvas.Children.AddRange(elementsToAdd);
    }

    private void AddRows(int numberToAdd)
    {
        List<Control> elementsToAdd = new();

        for (int row = 0; row < numberToAdd; row++)
        {
            int y = _renderedControls.Count;
            List<Control> newRow = new();
            for (int x = 0; x < _renderedControls.FirstOrDefault()?.Count; x++)
            {
                Control newItem = CreateItem(x, y);
                newRow.Add(newItem);
                elementsToAdd.Add(newItem);
            }

            _renderedControls.Add(newRow);
        }

        PART_ItemsCanvas.Children.AddRange(elementsToAdd);
    }

    private void RemoveColumns(int numberToRemove)
    {
        if (numberToRemove <= 0)
        {
            return;
        }

        List<Control> elementsToRemove = new();

        for (int y = 0; y < _renderedControls.Count; y++)
        {
            int currentRowBaseIndex = _renderedControls[y].Count - numberToRemove;
            for (int x = currentRowBaseIndex; x < currentRowBaseIndex + numberToRemove; x++)
            {
                elementsToRemove.Add(_renderedControls[y][x]);
            }

            _renderedControls[y].RemoveRange(currentRowBaseIndex, numberToRemove);
        }

        PART_ItemsCanvas.Children.RemoveAll(elementsToRemove);
    }

    private void RemoveRows(int numberToRemove)
    {
        if (numberToRemove <= 0)
        {
            return;
        }

        List<Control> elementsToRemove = _renderedControls.TakeLast(numberToRemove).SelectMany(y => y).ToList();

        _renderedControls.RemoveRange(_renderedControls.Count - numberToRemove, numberToRemove);

        PART_ItemsCanvas.Children.RemoveAll(elementsToRemove);
    }

    private void UpdateScrollViewerBoundaries()
    {
        PART_InnerCanvas.Width = GetItemsNumberX() * ItemWidth;
        PART_InnerCanvas.Height = GetItemsNumberY() * ItemHeight;
    }

    /// <summary>
    /// Assings proper DataContext to rendered <see cref="IControl"/>s objects.
    /// </summary>
    private void UpdatViewportDataContext()
    {
        // Iterate only through controls that are actually displayed; ignore those cached.
        for (int y = 0; y < Math.Min(GetNumberOfRenderedItemsVertically(), ResolveMaxVerticalVisibleItems()); y++)
        {
            for (int x = 0; x < Math.Min(GetNumberOfRenderedItemsHorizontally(), ResolveMaxHorizontalVisibleItems()); x++)
            {
                Control control = _renderedControls[y][x];
                int itemCoordX = ResolveHorizontalItemsOffset() + x;
                int itemCoordY = ResolveVerticalItemOffset() + y;

                // At the very end of scrolling we're exceeding by one (because of Smooth scrolling)
                // Here we ignore this one additional item
                if (itemCoordX >= GetItemsNumberX() || itemCoordY >= GetItemsNumberY())
                {
                    continue;
                }

                control.DataContext = GetItem(itemCoordX, itemCoordY);
            }
        }
    }

    private void ClearViewportDataContext()
    {
        _renderedControls.SelectMany(row => row).ToList().ForEach(c => c.DataContext = null);
    }

    private Control CreateItem(int x, int y)
    {
        ContentPresenter newItem = new();

        newItem.SetValue(Canvas.LeftProperty, x * ItemWidth);
        newItem.SetValue(Canvas.TopProperty, y * ItemHeight);
        newItem.SetValue(WidthProperty, ItemWidth);
        newItem.SetValue(HeightProperty, ItemHeight);
        newItem.SetValue(ContentPresenter.ContentTemplateProperty, ItemTemplate);

        newItem.PointerWheelChanged += (s, e) =>
        {
            e.Handled = true;
            HandleOffsetWhenScrollOnItem(e.Delta);
        };

        return newItem;
    }

    /// <summary>
    /// Updates the offset of main ScrollViewer using input data from clip ScrollViewer.
    /// </summary>
    /// <param name="delta"></param>
    private void HandleOffsetWhenScrollOnItem(Vector delta)
    {
        // TODO
        PART_ScrollViewer.Offset -= delta * 25;
    }

    private object? GetItem(int x, int y)
    {
        int index = y * GetItemsNumberX() + x;
        if (index >= Items.Count)
        {
            return null;
        }

        return Items[index];
    }

    /// <summary>
    /// How many elements can fit in current viewport horizontaly?
    /// </summary>
    private int GetMaxItemsInCurrentViewportX()
        => (int)Math.Ceiling(PART_ClipScrollViewer.Bounds.Width / ItemWidth);

    /// <summary>
    /// How many elements can fit in current viewport verticaly?
    /// </summary>
    private int GetMaxItemsInCurrentViewportY()
        => (int)Math.Ceiling(PART_ClipScrollViewer.Bounds.Height / ItemHeight);

    private int GetNumberOfRenderedItemsHorizontally() => _renderedControls.FirstOrDefault()?.Count ?? 0;

    private int GetNumberOfRenderedItemsVertically() => _renderedControls.Count;

    /// <summary>
    /// Calculates how many items from the left edge is current viewport (area with rendered <see cref="IControl"/>s). 
    /// </summary>
    private int ResolveHorizontalItemsOffset()
    {
        return (int)(PART_ScrollViewer.Offset.X / ItemWidth);
    }

    /// <summary>
    /// Calculates how many items from the top edge is current viewport (area with rendered <see cref="IControl"/>s). 
    /// </summary>
    private int ResolveVerticalItemOffset()
    {
        return (int)(PART_ScrollViewer.Offset.Y / ItemHeight);
    }

    /// <summary>
    /// Calculates how many <see cref="IControl"/>s should be rendered horizontaly.
    /// </summary>
    private int ResolveMaxHorizontalVisibleItems()
    {
        // How many items can fit in current viewport?
        int itemsCount = (int)Math.Ceiling(PART_ClipScrollViewer.Bounds.Width / ItemWidth);

        // Exceed viewport by one to enable smooth scrolling
        if (!DisableSmoothScrolling)
        {
            itemsCount++;
        }

        // If we can render more than we'd like to actually display we'll render only what is required
        if (itemsCount > GetItemsNumberX())
        {
            itemsCount = GetItemsNumberX();
        }

        return itemsCount;
    }

    /// <summary>
    /// Calculates how many <see cref="IControl"/>s should be rendered verticaly.
    /// </summary
    private int ResolveMaxVerticalVisibleItems()
    {
        // How many items can fit in current viewport?
        int itemsCount = (int)Math.Ceiling(PART_ClipScrollViewer.Bounds.Height / ItemHeight);

        // Exceed viewport by one to enable smooth scrolling
        if (!DisableSmoothScrolling)
        {
            itemsCount++;
        }

        // If we can render more than we'd like to actually display we'll render only what is required
        if (itemsCount > GetItemsNumberY())
        {
            itemsCount = GetItemsNumberY();
        }

        return itemsCount;
    }


    /// <summary>
    /// Returns number of elements in row for <see cref="Items"/> collection treated as 2D array.
    /// </summary>
    private int GetItemsNumberX() => Items.Count < MaxItemsInRow ? Items.Count : MaxItemsInRow;

    /// <summary>
    /// Returns number of elements in column for <see cref="Items"/> collection treated as 2D array.
    /// </summary>
    private int GetItemsNumberY() => (int)Math.Ceiling((double)Items.Count / MaxItemsInRow);
}