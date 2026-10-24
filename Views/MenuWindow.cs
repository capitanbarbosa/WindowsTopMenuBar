using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WindowsWhisker.Controls;
using WindowsWhisker.Models;
using WindowsWhisker.Native;
using WindowsWhisker.Services;

namespace WindowsWhisker.Views;

public sealed class MenuWindow : Window
{
    readonly Action _openSettings;
    readonly Action _quit;
    TextBox? _search;
    TextBlock? _watermark;
    ListBox? _categories;
    ListBox? _apps;
    TreeView? _tree;
    TextBlock? _empty;
    Border? _shell;
    string _categoryId;
    bool _holding;
    bool _dragged;
    bool _seenActive;
    bool _reloading;

    public MenuWindow(Action openSettings, Action quit)
    {
        _openSettings = openSettings;
        _quit = quit;
        _categoryId = SettingsStore.Instance.Config.Menu.DefaultCategory switch
        {
            "Recent" => "recent",
            "All" => "all",
            _ => "favorites"
        };
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        FontFamily = new FontFamily("Segoe UI");
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        SettingsStore.Instance.AreaChanged += OnConfig;
        AppCatalogService.Instance.Changed += OnCatalog;
        Closed += (_, _) =>
        {
            SettingsStore.Instance.AreaChanged -= OnConfig;
            AppCatalogService.Instance.Changed -= OnCatalog;
        };
        Content = Build();
        Loaded += (_, _) => _search?.Focus();
    }

    public void Place(FrameworkElement anchor)
    {
        var config = SettingsStore.Instance.Config.Menu;
        var source = PresentationSource.FromVisual(anchor);
        var hwnd = new System.Windows.Interop.WindowInteropHelper(Window.GetWindow(anchor)).Handle;
        uint dpi = Win32.GetDpiForWindow(hwnd);
        if (dpi == 0) dpi = 96;
        double scale = dpi / 96.0;
        var monitors = Win32.GetMonitors();
        var origin = anchor.PointToScreen(new Point(0, 0));
        var mon = monitors.FirstOrDefault(m => origin.X >= m.Bounds.Left && origin.X < m.Bounds.Right && origin.Y >= m.Bounds.Top && origin.Y < m.Bounds.Bottom);
        if (mon.Handle == IntPtr.Zero && monitors.Count > 0) mon = monitors[0];

        double maxW = mon.Handle == IntPtr.Zero ? 1200 : mon.Bounds.Width / scale - 16;
        double maxH = mon.Handle == IntPtr.Zero ? 800 : mon.Bounds.Height / scale - 16;
        Width = Math.Clamp(config.MenuWidth, 360, Math.Max(360, maxW));
        Height = Math.Clamp(config.MenuHeight, 320, Math.Max(320, maxH));

        if (source == null)
        {
            Left = 40;
            Top = 40;
            return;
        }
        var toDip = source.CompositionTarget.TransformFromDevice;
        string edge = SettingsStore.Instance.Config.Panel.Edge;
        if (edge == "Left")
        {
            var p = toDip.Transform(anchor.PointToScreen(new Point(anchor.ActualWidth, 0)));
            Left = p.X;
            Top = p.Y;
        }
        else if (edge == "Right")
        {
            var p = toDip.Transform(anchor.PointToScreen(new Point(0, 0)));
            Left = p.X - Width;
            Top = p.Y;
        }
        else if (edge == "Bottom")
        {
            var p = toDip.Transform(anchor.PointToScreen(new Point(0, 0)));
            Left = p.X;
            Top = p.Y - Height;
        }
        else
        {
            var p = toDip.Transform(anchor.PointToScreen(new Point(0, anchor.ActualHeight)));
            Left = p.X;
            Top = p.Y;
        }

        if (mon.Handle != IntPtr.Zero)
        {
            double left = mon.Bounds.Left / scale;
            double top = mon.Bounds.Top / scale;
            double right = mon.Bounds.Right / scale;
            double bottom = mon.Bounds.Bottom / scale;
            if (Left + Width > right) Left = right - Width;
            if (Top + Height > bottom) Top = bottom - Height;
            if (Left < left) Left = left;
            if (Top < top) Top = top;
        }
    }

    public void Reload()
    {
        if (_reloading) return;
        string search = _search?.Text ?? "";
        _reloading = true;
        Content = Build();
        _reloading = false;
        if (_search != null)
        {
            _search.Text = search;
            _search.Focus();
        }
    }

    void OnConfig(ConfigArea area)
    {
        if (area != ConfigArea.Menu || _reloading) return;
        Dispatcher.BeginInvoke(Reload);
    }

    void OnCatalog() => Dispatcher.BeginInvoke(Reload);

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        _seenActive = true;
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (!_seenActive || SettingsStore.Instance.Config.Menu.StayVisibleOnFocusLoss) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (_holding || OwnedWindows.Count > 0 || !IsLoaded) return;
            Close();
        });
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    UIElement Build()
    {
        var config = SettingsStore.Instance.Config.Menu;
        byte alpha = (byte)Math.Clamp(config.BackgroundOpacity * 255 / 100, 0, 255);
        _shell = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderBrush = Ui.Line,
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromArgb(alpha, 0x2B, 0x2D, 0x36)),
            ClipToBounds = true
        };

        _search = new TextBox { FontSize = 14, Padding = new Thickness(8, 6, 8, 6) };
        _watermark = new TextBlock
        {
            Text = "Search applications",
            Foreground = Ui.Muted,
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        _search.TextChanged += (_, _) =>
        {
            if (_watermark != null)
                _watermark.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (!_reloading) FillApps();
        };
        _search.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Down && _apps is { Items.Count: > 0 })
            {
                _apps.SelectedIndex = 0;
                _apps.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                LaunchSelected();
                e.Handled = true;
            }
        };

        var searchGrid = new Grid { Margin = new Thickness(12, 12, 12, 8) };
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var searchHost = new Grid();
        searchHost.Children.Add(_search);
        searchHost.Children.Add(_watermark);
        searchGrid.Children.Add(searchHost);
        if (config.CommandsNextToSearch)
        {
            var commands = CommandBar(iconOnly: false);
            Grid.SetColumn(commands, 1);
            searchGrid.Children.Add(commands);
        }

        var profile = BuildProfile(config);
        _categories = new ListBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Margin = new Thickness(6, 0, 0, 0) };
        _apps = new ListBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Margin = new Thickness(4, 0, 6, 0) };
        _tree = new TreeView { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Ui.Text, Margin = new Thickness(8, 0, 8, 0), Visibility = Visibility.Collapsed };
        _empty = new TextBlock
        {
            Foreground = Ui.Muted,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(24),
            IsHitTestVisible = false
        };
        _apps.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                LaunchSelected();
                e.Handled = true;
            }
        };
        _categories.SelectionChanged += (_, _) =>
        {
            if (_reloading) return;
            if (_categories?.SelectedItem is ListBoxItem item && item.Tag is string id)
            {
                _categoryId = id;
                if (_search != null && _search.Text.Length > 0)
                    _search.Text = "";
                else
                    FillApps();
            }
        };

        var appHost = new Grid();
        appHost.Children.Add(_apps);
        appHost.Children.Add(_tree);
        appHost.Children.Add(_empty);
        var body = BuildBody(config, appHost);

        var dock = new DockPanel();
        bool searchBottom = config.SearchOnBottom;
        bool profileBottom = config.ProfileOnBottom;
        if (profile != null)
            DockPanel.SetDock(profile, profileBottom ? Dock.Bottom : Dock.Top);
        DockPanel.SetDock(searchGrid, searchBottom ? Dock.Bottom : Dock.Top);
        if (searchBottom && profileBottom && profile != null)
        {
            dock.Children.Add(profile);
            dock.Children.Add(searchGrid);
        }
        else if (!searchBottom && !profileBottom && profile != null)
        {
            dock.Children.Add(searchGrid);
            dock.Children.Add(profile);
        }
        else
        {
            dock.Children.Add(searchGrid);
            if (profile != null) dock.Children.Add(profile);
        }
        dock.Children.Add(body);
        _shell.Child = dock;
        FillCategories();
        FillApps();
        return _shell;
    }

    FrameworkElement? BuildProfile(MenuConfig config)
    {
        bool commandsHere = !config.CommandsNextToSearch;
        if (config.ProfileStyle == "Hidden" && !commandsHere)
        {
            var only = new Grid { Margin = new Thickness(12, 8, 12, 12) };
            only.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            only.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var quit = QuitButton(iconOnly: true);
            Grid.SetColumn(quit, 1);
            only.Children.Add(quit);
            return only;
        }
        var grid = new Grid { Margin = new Thickness(12, 8, 12, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (config.ProfileStyle != "Hidden")
        {
            string name = Ui.UserName();
            string initial = name.Length == 0 ? "?" : name[..1].ToUpperInvariant();
            var avatar = new Grid { Width = 36, Height = 36, Margin = new Thickness(0, 0, 10, 0) };
            avatar.Children.Add(new Border
            {
                Background = Ui.Accent(),
                CornerRadius = new CornerRadius(config.ProfileStyle == "Round" ? 18 : 4)
            });
            avatar.Children.Add(new TextBlock
            {
                Text = initial,
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
            grid.Children.Add(avatar);
        }
        var nameBlock = new TextBlock
        {
            Text = Ui.UserName(),
            Foreground = Ui.Text,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(nameBlock, 1);
        grid.Children.Add(nameBlock);
        if (commandsHere)
        {
            var bar = CommandBar(iconOnly: true);
            Grid.SetColumn(bar, 2);
            grid.Children.Add(bar);
        }
        return grid;
    }

    StackPanel CommandBar(bool iconOnly)
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var cmd in SettingsStore.Instance.Config.Menu.Commands.Where(c => c.Visible))
        {
            var button = new Button
            {
                ToolTip = cmd.Name,
                Margin = new Thickness(3, 0, 0, 0),
                Padding = iconOnly ? new Thickness(0) : new Thickness(8, 4, 8, 4),
                Width = iconOnly ? 32 : double.NaN,
                Height = iconOnly ? 32 : double.NaN,
                Content = iconOnly
                    ? Ui.GlyphText(cmd.Kind, 14)
                    : cmd.Name
            };
            var captured = cmd;
            button.Click += (_, _) => RunCommand(captured);
            bar.Children.Add(button);
        }
        bar.Children.Add(QuitButton(iconOnly));
        return bar;
    }

    Button QuitButton(bool iconOnly)
    {
        var button = new Button
        {
            ToolTip = "Quit Windows Whisker",
            Margin = new Thickness(8, 0, 0, 0),
            Padding = iconOnly ? new Thickness(0) : new Thickness(8, 4, 8, 4),
            Width = iconOnly ? 32 : double.NaN,
            Height = iconOnly ? 32 : double.NaN,
            Content = iconOnly ? Ui.GlyphText("quit", 14) : "Quit"
        };
        button.Click += (_, _) => _quit();
        return button;
    }

    FrameworkElement BuildBody(MenuConfig config, Grid appHost)
    {
        if (config.ViewMode == "Tree") return appHost;
        var body = new Grid();
        if (config.CategoriesHorizontal)
        {
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var factory = new FrameworkElementFactory(typeof(StackPanel));
            factory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            _categories!.ItemsPanel = new ItemsPanelTemplate(factory);
            _categories.MaxHeight = 56;
            ScrollViewer.SetHorizontalScrollBarVisibility(_categories, ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollBarVisibility(_categories, ScrollBarVisibility.Disabled);
            body.Children.Add(_categories);
            Grid.SetRow(appHost, 1);
            body.Children.Add(appHost);
            return body;
        }

        double col = config.ShowCategoryNames ? 210 : 68;
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(col) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bool left = config.CategoriesOnLeft;
        _categories!.BorderBrush = Ui.Line;
        _categories.BorderThickness = new Thickness(left ? 0 : 1, 0, left ? 1 : 0, 0);
        Grid.SetColumn(_categories, left ? 0 : 1);
        Grid.SetColumn(appHost, left ? 1 : 0);
        body.Children.Add(_categories);
        body.Children.Add(appHost);
        return body;
    }

    void FillCategories()
    {
        if (_categories == null) return;
        _reloading = true;
        _categories.Items.Clear();
        var config = SettingsStore.Instance.Config.Menu;
        var apps = AppCatalogService.Instance.Snapshot();
        var cats = new List<(string Id, string Name, string Glyph)>
        {
            ("favorites", "Favorites", "favorites"),
            ("recent", "Recently Used", "recent"),
            ("all", "All Applications", "all")
        };
        var folders = new List<string>();
        foreach (var app in apps)
        {
            if (string.IsNullOrWhiteSpace(app.Category) || app.Category == "Applications") continue;
            if (!folders.Contains(app.Category)) folders.Add(app.Category);
        }
        if (config.SortCategories)
            folders.Sort(StringComparer.CurrentCultureIgnoreCase);
        foreach (var folder in folders)
            cats.Add((folder, folder, "folder"));

        if (cats.All(c => c.Id != _categoryId))
            _categoryId = "all";

        int size = MenuConfig.IconPx(config.CategoryIconSize);
        foreach (var cat in cats)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var glyph = Ui.GlyphText(cat.Glyph, Math.Max(14, size));
            glyph.Width = Math.Max(16, size);
            row.Children.Add(glyph);
            if (config.ShowCategoryNames && !config.CategoriesHorizontal)
            {
                row.Children.Add(new TextBlock
                {
                    Text = cat.Name,
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Ui.Text
                });
            }
            var item = new ListBoxItem
            {
                Content = config.CategoriesHorizontal || !config.ShowCategoryNames ? glyph : row,
                Tag = cat.Id,
                ToolTip = config.ShowTooltips ? cat.Name : null
            };
            item.MouseEnter += (_, _) =>
            {
                if (!SettingsStore.Instance.Config.Menu.HoverSwitch || _reloading) return;
                if (_search != null && _search.IsFocused && _search.Text.Length > 0) return;
                item.IsSelected = true;
            };
            _categories.Items.Add(item);
            if (cat.Id == _categoryId) item.IsSelected = true;
        }
        _reloading = false;
    }

    void FillApps()
    {
        if (_apps == null || _tree == null || _empty == null) return;
        var config = SettingsStore.Instance.Config.Menu;
        var apps = Filter().ToList();
        _empty.Text = EmptyText(apps.Count);
        if (config.ViewMode == "Tree")
        {
            _apps.Visibility = Visibility.Collapsed;
            _tree.Visibility = Visibility.Visible;
            _empty.Visibility = apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            BuildTree(apps);
            return;
        }

        _tree.Visibility = Visibility.Collapsed;
        _apps.Visibility = Visibility.Visible;
        bool icons = config.ViewMode == "Icons";
        var panel = new FrameworkElementFactory(icons ? typeof(WrapPanel) : typeof(VirtualizingStackPanel));
        _apps.ItemsPanel = new ItemsPanelTemplate(panel);
        _apps.Items.Clear();
        foreach (var app in apps)
            _apps.Items.Add(MakeItem(app, icons));
        _empty.Visibility = apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    IEnumerable<AppEntry> Filter()
    {
        var config = SettingsStore.Instance.Config.Menu;
        var stored = SettingsStore.Instance.Config;
        var all = AppCatalogService.Instance.Snapshot();
        string query = _search?.Text.Trim() ?? "";
        if (query.Length > 0)
        {
            var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return all.Where(app => tokens.All(token => Matches(app, token, config)));
        }
        if (_categoryId == "favorites")
        {
            var list = new List<AppEntry>();
            foreach (var key in stored.Favorites)
            {
                var app = all.FirstOrDefault(a => a.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                if (app != null) list.Add(app);
            }
            return list;
        }
        if (_categoryId == "recent")
        {
            var list = new List<AppEntry>();
            foreach (var key in stored.Recent)
            {
                if (list.Count >= config.RecentCount) break;
                if (!config.RecentIncludeFavorites && SettingsStore.Instance.IsFavorite(key)) continue;
                var app = all.FirstOrDefault(a => a.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                if (app != null) list.Add(app);
            }
            return list;
        }
        if (_categoryId == "all") return all;
        return all.Where(a => a.Category == _categoryId);
    }

    static bool Matches(AppEntry app, string token, MenuConfig config)
    {
        if (app.Name.Contains(token, StringComparison.OrdinalIgnoreCase)) return true;
        if (app.GenericName.Contains(token, StringComparison.OrdinalIgnoreCase)) return true;
        if (app.Category.Contains(token, StringComparison.OrdinalIgnoreCase)) return true;
        if (config.SearchDescriptions && app.Description.Contains(token, StringComparison.OrdinalIgnoreCase)) return true;
        if (config.SearchExecutables &&
            ((app.TargetPath?.Contains(token, StringComparison.OrdinalIgnoreCase) ?? false) ||
             (app.AppUserModelId?.Contains(token, StringComparison.OrdinalIgnoreCase) ?? false)))
            return true;
        return false;
    }

    string EmptyText(int count)
    {
        if (count > 0) return "";
        if (!AppCatalogService.Instance.IsReady) return "Loading applications…";
        if ((_search?.Text.Length ?? 0) > 0) return "No matching applications.";
        return _categoryId switch
        {
            "favorites" => "No favorites yet. Right-click an application and choose Add to favorites.",
            "recent" => "Applications you open will show up here.",
            _ => "This category is empty."
        };
    }

    ListBoxItem MakeItem(AppEntry app, bool icons)
    {
        var config = SettingsStore.Instance.Config.Menu;
        int size = MenuConfig.IconPx(config.AppIconSize);
        var image = new Image
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            Source = IconService.Get(app, size)
        };
        FrameworkElement content;
        if (icons)
        {
            var stack = new StackPanel { Width = Math.Max(84, size + 28), Margin = new Thickness(6) };
            image.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(image);
            stack.Children.Add(new TextBlock
            {
                Text = app.DisplayName(config.ShowGenericNames),
                Foreground = Ui.Text,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 36,
                Margin = new Thickness(0, 6, 0, 0),
                FontSize = 11
            });
            content = stack;
        }
        else
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            image.Margin = new Thickness(0, 0, 10, 0);
            image.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(image);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock
            {
                Text = app.DisplayName(config.ShowGenericNames),
                Foreground = Ui.Text,
                FontSize = 13,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            if (config.ShowDescriptions && !string.IsNullOrWhiteSpace(app.Description))
            {
                text.Children.Add(new TextBlock
                {
                    Text = app.Description,
                    Foreground = Ui.Muted,
                    FontSize = 11,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            }
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            if (SettingsStore.Instance.IsFavorite(app.Key))
            {
                var star = Ui.GlyphText("favorites", 12);
                star.Foreground = Ui.Accent();
                star.Margin = new Thickness(8, 0, 0, 0);
                Grid.SetColumn(star, 2);
                grid.Children.Add(star);
            }
            content = grid;
        }

        var item = new ListBoxItem { Content = content, Tag = app };
        if (config.ShowTooltips)
        {
            string tip = !string.IsNullOrWhiteSpace(app.Description) ? app.Description : app.TargetPath;
            if (!string.IsNullOrWhiteSpace(tip)) item.ToolTip = tip;
        }
        Point start = default;
        item.PreviewMouseLeftButtonDown += (_, e) => { start = e.GetPosition(item); _dragged = false; };
        item.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            var now = e.GetPosition(item);
            if (Math.Abs(now.X - start.X) + Math.Abs(now.Y - start.Y) < 8) return;
            _dragged = true;
            DragDrop.DoDragDrop(item, new DataObject(Ui.AppDragFormat, app.Key), DragDropEffects.Copy);
        };
        item.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (_dragged) { _dragged = false; return; }
            Launch(app);
            e.Handled = true;
        };
        item.MouseRightButtonUp += (_, e) =>
        {
            ShowAppMenu(app, item);
            e.Handled = true;
        };
        return item;
    }

    void BuildTree(List<AppEntry> apps)
    {
        if (_tree == null) return;
        _tree.Items.Clear();
        foreach (var group in apps.GroupBy(a => string.IsNullOrWhiteSpace(a.Category) ? "Applications" : a.Category).OrderBy(g => g.Key))
        {
            var node = new TreeViewItem { Header = group.Key, IsExpanded = true, Foreground = Ui.Text };
            foreach (var app in group)
            {
                var child = new TreeViewItem { Header = app.DisplayName(SettingsStore.Instance.Config.Menu.ShowGenericNames), Tag = app, Foreground = Ui.Text };
                child.MouseDoubleClick += (_, e) =>
                {
                    Launch(app);
                    e.Handled = true;
                };
                child.MouseLeftButtonUp += (_, _) => Launch(app);
                node.Items.Add(child);
            }
            _tree.Items.Add(node);
        }
    }

    void ShowAppMenu(AppEntry app, FrameworkElement anchor)
    {
        bool fav = SettingsStore.Instance.IsFavorite(app.Key);
        var menu = new ContextMenu();
        menu.Items.Add(Item("Launch", () => Launch(app)));
        menu.Items.Add(Item("Pin to panel", () =>
        {
            if (Application.Current.MainWindow is PanelWindow panel)
                panel.PinApp(app);
        }));
        menu.Items.Add(Item(fav ? "Remove from favorites" : "Add to favorites", () => SettingsStore.Instance.ToggleFavorite(app.Key)));
        menu.Items.Add(Item("Open file location", () => OpenLocation(app)));
        menu.PlacementTarget = anchor;
        menu.IsOpen = true;
    }

    static void OpenLocation(AppEntry app)
    {
        string path = File.Exists(app.TargetPath) ? app.TargetPath : app.Key;
        if (!File.Exists(path)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
    }

    void LaunchSelected()
    {
        if (_apps?.SelectedItem is ListBoxItem item && item.Tag is AppEntry selected)
        {
            Launch(selected);
            return;
        }
        var first = Filter().FirstOrDefault();
        if (first != null) Launch(first);
    }

    void Launch(AppEntry app)
    {
        try
        {
            AppCatalogService.Launch(app);
            SettingsStore.Instance.PushRecent(app.Key);
            Close();
        }
        catch (Exception ex)
        {
            _holding = true;
            Dialogs.Alert(this, "Couldn't launch " + app.Name + ".\n" + ex.Message);
            _holding = false;
        }
    }

    void RunCommand(CommandItem cmd)
    {
        var config = SettingsStore.Instance.Config.Menu;
        if (cmd.Confirm && config.ConfirmSessionCommands)
        {
            _holding = true;
            bool ok = Dialogs.Ask(this, "Are you sure you want to " + cmd.Name.ToLowerInvariant() + "?");
            _holding = false;
            if (!ok) return;
        }
        try
        {
            if (cmd.Kind == "settings")
            {
                Close();
                _openSettings();
                return;
            }
            SessionCommands.Execute(cmd, _openSettings);
            Close();
        }
        catch (Exception ex)
        {
            _holding = true;
            Dialogs.Alert(this, ex.Message);
            _holding = false;
        }
    }

    static MenuItem Item(string header, Action action)
    {
        var entry = new MenuItem { Header = header };
        entry.Click += (_, _) => action();
        return entry;
    }
}
