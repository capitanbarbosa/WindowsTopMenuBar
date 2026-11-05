using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using WindowsTopMenuBar.Controls;
using WindowsTopMenuBar.Models;
using WindowsTopMenuBar.Services;

namespace WindowsTopMenuBar.Views;

public static class Dialogs
{
    public static bool Ask(Window? owner, string message)
    {
        bool yes = false;
        var window = Create(owner, "Confirm", 440, 180);
        var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = Ui.Text, FontSize = 14, Margin = new Thickness(18, 16, 18, 8) };
        var buttons = Buttons();
        var no = new Button { Content = "Cancel", Width = 90, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var ok = new Button { Content = "Yes", Width = 90, IsDefault = true };
        no.Click += (_, _) => window.Close();
        ok.Click += (_, _) => { yes = true; window.Close(); };
        buttons.Children.Add(no);
        buttons.Children.Add(ok);
        window.Content = Stack(text, buttons);
        window.ShowDialog();
        return yes;
    }

    public static void Alert(Window? owner, string message)
    {
        var window = Create(owner, "Windows Top Menu Bar", 440, 180);
        var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = Ui.Text, FontSize = 14, Margin = new Thickness(18, 16, 18, 8) };
        var buttons = Buttons();
        var ok = new Button { Content = "OK", Width = 90, IsDefault = true, IsCancel = true };
        ok.Click += (_, _) => window.Close();
        buttons.Children.Add(ok);
        window.Content = Stack(text, buttons);
        window.ShowDialog();
    }

    public static string? Prompt(Window? owner, string title, string initial)
    {
        string? value = null;
        var window = Create(owner, title, 420, 170);
        var box = new TextBox { Text = initial, Margin = new Thickness(18, 16, 18, 8) };
        var buttons = Buttons();
        var cancel = new Button { Content = "Cancel", Width = 90, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var ok = new Button { Content = "OK", Width = 90, IsDefault = true };
        cancel.Click += (_, _) => window.Close();
        ok.Click += (_, _) => { value = box.Text; window.Close(); };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        window.Content = Stack(box, buttons);
        window.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        window.ShowDialog();
        return value;
    }

    public static AppEntry? Pick(Window? owner)
    {
        AppEntry? chosen = null;
        var window = Create(owner, "Add a shortcut", 460, 560);
        var search = new TextBox { Margin = new Thickness(12, 12, 12, 8) };
        var list = new ListBox { Margin = new Thickness(12, 0, 12, 8) };
        var buttons = Buttons();
        var browse = new Button { Content = "Browse…", Width = 90, Margin = new Thickness(0, 0, 8, 0), HorizontalAlignment = HorizontalAlignment.Left };
        var cancel = new Button { Content = "Cancel", Width = 90, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var add = new Button { Content = "Add", Width = 90, IsDefault = true };
        DockPanel.SetDock(browse, Dock.Left);
        buttons.Children.Insert(0, browse);

        void Refill()
        {
            string q = search.Text.Trim();
            var apps = AppCatalogService.Instance.Snapshot();
            if (q.Length > 0)
                apps = apps.Where(a => a.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || a.Category.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
            list.ItemsSource = apps.Take(400).ToList();
        }

        list.DisplayMemberPath = "Name";
        search.TextChanged += (_, _) => Refill();
        Refill();
        void Accept()
        {
            if (list.SelectedItem is AppEntry app)
            {
                chosen = app;
                window.Close();
            }
        }
        list.MouseDoubleClick += (_, _) => Accept();
        add.Click += (_, _) => Accept();
        cancel.Click += (_, _) => window.Close();
        browse.Click += (_, _) =>
        {
            var dlg = new OpenFileDialog { Filter = "Programs|*.exe;*.lnk|All files|*.*" };
            if (dlg.ShowDialog(window) == true)
            {
                chosen = new AppEntry
                {
                    Key = dlg.FileName,
                    Name = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName),
                    GenericName = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName),
                    TargetPath = dlg.FileName,
                    IconPath = dlg.FileName,
                    Category = "Applications"
                };
                window.Close();
            }
        };

        var root = new DockPanel();
        DockPanel.SetDock(search, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        buttons.Children.Add(cancel);
        buttons.Children.Add(add);
        root.Children.Add(search);
        root.Children.Add(buttons);
        root.Children.Add(list);
        window.Content = root;
        window.Loaded += (_, _) => search.Focus();
        window.ShowDialog();
        return chosen;
    }

    public static void SeparatorProperties(Window? owner, PanelItem item)
    {
        var window = Create(owner, "Separator", 320, 240);
        var styleBox = new ComboBox { Width = 160, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var style in new[] { "Separator", "Transparent", "Handle", "Dots" })
            styleBox.Items.Add(new ComboBoxItem { Content = style, Tag = style });
        foreach (ComboBoxItem choice in styleBox.Items)
        {
            if ((choice.Tag as string) == item.Style)
                styleBox.SelectedItem = choice;
        }
        if (styleBox.SelectedIndex < 0) styleBox.SelectedIndex = 0;
        var expand = new CheckBox { Content = "Expand", IsChecked = item.Expand, Margin = new Thickness(0, 8, 0, 0) };

        void Save()
        {
            item.Style = (styleBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "Separator";
            item.Expand = expand.IsChecked == true;
            SettingsStore.Instance.Update(_ => { }, ConfigArea.PanelItems);
        }

        styleBox.SelectionChanged += (_, _) => Save();
        expand.Click += (_, _) => Save();

        var body = new StackPanel { Margin = new Thickness(16, 12, 16, 8) };
        body.Children.Add(new TextBlock { Text = "Appearance", Style = (Style)Application.Current.FindResource("GroupHeader") });
        var styleRow = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        styleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        styleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var styleLabel = new TextBlock { Text = "Style:", Style = (Style)Application.Current.FindResource("FieldLabel") };
        Grid.SetColumn(styleBox, 1);
        styleRow.Children.Add(styleLabel);
        styleRow.Children.Add(styleBox);
        body.Children.Add(styleRow);
        body.Children.Add(expand);

        var buttons = new DockPanel { Margin = new Thickness(12, 0, 12, 12), LastChildFill = false };
        var help = new Button { Content = "Help", Width = 84 };
        var close = new Button { Content = "Close", Width = 90, IsCancel = true, IsDefault = true };
        DockPanel.SetDock(help, Dock.Left);
        DockPanel.SetDock(close, Dock.Right);
        buttons.Children.Add(help);
        buttons.Children.Add(close);
        help.Click += (_, _) => Help(window);
        close.Click += (_, _) => window.Close();

        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(body);
        window.Content = root;
        window.ShowDialog();
    }

    public static void AddPanelItems(Window? owner, Action<string> add)
    {
        var window = Create(owner, "Add New Items", 480, 540);
        var searchRow = new DockPanel { Margin = new Thickness(12, 12, 12, 8) };
        var searchLabel = new TextBlock
        {
            Text = "Search:",
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        var search = new TextBox { Foreground = Brushes.White };
        DockPanel.SetDock(searchLabel, Dock.Left);
        searchRow.Children.Add(searchLabel);
        searchRow.Children.Add(search);

        var list = new ListBox { Margin = new Thickness(12, 0, 12, 8) };

        void Refill()
        {
            string query = search.Text.Trim();
            string? selected = (list.SelectedItem as ListBoxItem)?.Tag as string;
            list.Items.Clear();
            foreach (var plugin in PanelPlugins.All)
            {
                if (query.Length > 0 &&
                    !plugin.Name.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                    !plugin.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
                    continue;
                var row = new StackPanel { Margin = new Thickness(2, 3, 2, 3) };
                var title = new StackPanel { Orientation = Orientation.Horizontal };
                title.Children.Add(new TextBlock
                {
                    Text = plugin.Glyph,
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 16,
                    Width = 26,
                    Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center
                });
                title.Children.Add(new TextBlock
                {
                    Text = plugin.Name,
                    Foreground = Brushes.White,
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                });
                row.Children.Add(title);
                row.Children.Add(new TextBlock
                {
                    Text = plugin.Description,
                    Foreground = Brushes.White,
                    FontSize = 12,
                    Margin = new Thickness(26, 2, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                });
                var entry = new ListBoxItem { Content = row, Tag = plugin.Kind };
                list.Items.Add(entry);
                if (plugin.Kind == selected) list.SelectedItem = entry;
            }
            if (list.SelectedIndex < 0 && list.Items.Count > 0) list.SelectedIndex = 0;
        }

        void Commit()
        {
            if (list.SelectedItem is ListBoxItem entry && entry.Tag is string kind)
                add(kind);
        }

        search.TextChanged += (_, _) => Refill();
        list.MouseDoubleClick += (_, _) => Commit();

        var buttons = new DockPanel { Margin = new Thickness(12, 0, 12, 12), LastChildFill = false };
        var help = new Button { Content = "Help", Width = 84, HorizontalAlignment = HorizontalAlignment.Left };
        var addButton = new Button { Content = "+  Add", Width = 96, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var close = new Button { Content = "Close", Width = 90, IsCancel = true };
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        right.Children.Add(addButton);
        right.Children.Add(close);
        DockPanel.SetDock(help, Dock.Left);
        buttons.Children.Add(help);
        buttons.Children.Add(right);
        help.Click += (_, _) => Help(window);
        addButton.Click += (_, _) => Commit();
        close.Click += (_, _) => window.Close();

        var root = new DockPanel();
        DockPanel.SetDock(searchRow, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(searchRow);
        root.Children.Add(buttons);
        root.Children.Add(list);
        window.Content = root;
        Refill();
        window.Loaded += (_, _) => search.Focus();
        window.ShowDialog();
    }

    public static void Help(Window? owner)
    {
        var window = Create(owner, "Windows Top Menu Bar", 520, 420);
        var text = new TextBlock
        {
            Text = "This is a Kali-style panel for Windows.\n\n" +
                   "The bar keeps a reserved strip of the desktop, so maximized windows sit against it instead of covering it.\n\n" +
                   "Panel settings has the display options: lock the panel, set the row size, length, opacity, and color. While the panel is unlocked you can also drag elements and drag the inner edge to change the row size.\n\n" +
                   "The Items page lists the panel contents. Drag a row, or use the arrows, to reorder it. Add opens Add New Items, where you can search for a separator, a clipboard shortcut, a launcher, and the other panel pieces.\n\n" +
                   "Add launchers from the panel menu, from Panel settings, or by right-clicking an application and choosing Pin to panel. You can also drag an application out of the menu onto the bar.\n\n" +
                   "The application menu shows favorites, recently used, and all applications, with Settings and Quit along the bottom.\n\n" +
                   "The desktop shifter is the row of numbered desktops. Click one to switch, scroll to move between them, and right-click to add, rename, or remove a desktop.\n\n" +
                   "Crashes and unexpected errors are written to %AppData%\\WindowsTopMenuBar\\crash.txt. Everyday errors go in log.txt next to it. Right-click the bar and choose Open crash log to view the latest crash.\n\n" +
                   "Quit from the button in the application menu, or from the panel's right-click menu. That gives the reserved space back to Windows.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Ui.Text,
            FontSize = 13,
            Margin = new Thickness(18, 14, 18, 8)
        };
        var buttons = Buttons();
        var ok = new Button { Content = "Close", Width = 90, IsDefault = true, IsCancel = true };
        ok.Click += (_, _) => window.Close();
        buttons.Children.Add(ok);
        var scroll = new ScrollViewer { Content = text, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        window.Content = Stack(scroll, buttons);
        window.ShowDialog();
    }

    static Window Create(Window? owner, string title, double width, double height)
    {
        var window = new Window
        {
            Title = title,
            Width = width,
            Height = height,
            WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Owner = owner,
            ResizeMode = ResizeMode.NoResize,
            Background = Ui.Window,
            Foreground = Ui.Text,
            FontFamily = new FontFamily("Segoe UI"),
            ShowInTaskbar = false,
            Topmost = true
        };
        Ui.DarkTitle(window);
        return window;
    }

    static DockPanel Buttons()
    {
        var dock = new DockPanel { Margin = new Thickness(12, 0, 12, 12), LastChildFill = false, HorizontalAlignment = HorizontalAlignment.Right };
        return dock;
    }

    static DockPanel Stack(UIElement body, DockPanel buttons)
    {
        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(body);
        return root;
    }
}
