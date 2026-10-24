using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using WindowsWhisker.Controls;
using WindowsWhisker.Models;
using WindowsWhisker.Native;
using WindowsWhisker.Services;

namespace WindowsWhisker.Views;

public partial class SettingsWindow : Window
{
    bool _loading;
    string? _keepElementId;

    public SettingsWindow(string tab)
    {
        InitializeComponent();
        Ui.DarkTitle(this);
        FillIconSizes(AppIconBox);
        FillIconSizes(CategoryIconBox);
        FillMonitors();
        LoadUi();
        ShowPanelSub("display");
        SelectTab(string.IsNullOrWhiteSpace(tab) ? "panel" : tab);
        SettingsStore.Instance.AreaChanged += OnExternal;
        Closed += (_, _) => SettingsStore.Instance.AreaChanged -= OnExternal;
    }

    public void SelectTab(string tab)
    {
        PagePanel.Visibility = tab == "panel" ? Visibility.Visible : Visibility.Collapsed;
        PageGeneral.Visibility = tab == "general" ? Visibility.Visible : Visibility.Collapsed;
        PageAppearance.Visibility = tab == "appearance" ? Visibility.Visible : Visibility.Collapsed;
        PageBehavior.Visibility = tab == "behavior" ? Visibility.Visible : Visibility.Collapsed;
        PageCommands.Visibility = tab == "commands" ? Visibility.Visible : Visibility.Collapsed;
        PageSearch.Visibility = tab == "search" ? Visibility.Visible : Visibility.Collapsed;
        TabPanel.IsChecked = tab == "panel";
        TabGeneral.IsChecked = tab == "general";
        TabAppearance.IsChecked = tab == "appearance";
        TabBehavior.IsChecked = tab == "behavior";
        TabCommands.IsChecked = tab == "commands";
        TabSearch.IsChecked = tab == "search";
    }

    void Tab_Click(object sender, RoutedEventArgs e) => SelectTab((string)((FrameworkElement)sender).Tag);

    void OnExternal(ConfigArea area)
    {
        if (area == ConfigArea.PanelItems)
            RefreshElements();
    }

    void LoadUi()
    {
        _loading = true;
        var config = SettingsStore.Instance.Config;
        FillProfiles();
        SelectTag(ModeBox, config.Panel.Mode);
        SelectTag(EdgeBox, config.Panel.Edge);
        SelectTag(MonitorBox, config.Panel.MonitorDevice);
        if (MonitorBox.SelectedIndex < 0 && MonitorBox.Items.Count > 0)
            MonitorBox.SelectedIndex = 0;
        LockBox.IsChecked = config.Panel.Locked;
        SelectTag(HideBox, config.Panel.AutoHideMode);
        ReserveBox.IsChecked = config.Panel.ReserveSpace;
        StartupBox.IsChecked = config.RunAtStartup;
        RowSizeBox.Text = config.Panel.Height.ToString();
        RowsBox.Text = config.Panel.Rows.ToString();
        BorderBox.Text = config.Panel.BorderWidth.ToString();
        LengthSlider.Value = config.Panel.Length;
        LengthValue.Text = config.Panel.Length.ToString();
        LengthSlider.IsEnabled = !config.Panel.AutoLength;
        AutoLengthBox.IsChecked = config.Panel.AutoLength;
        DarkBox.IsChecked = config.Panel.DarkMode;
        SelectTag(BackgroundStyleBox, config.Panel.BackgroundStyle);
        PaintSwatch(config.Panel.Background);
        SelectColor(AccentBox, config.Panel.Accent);
        AutoIconBox.IsChecked = config.Panel.AutoIconSize;
        EnterOpacitySlider.Value = config.Panel.OpacityEnter;
        LeaveOpacitySlider.Value = config.Panel.OpacityLeave;
        EnterOpacityValue.Text = config.Panel.OpacityEnter.ToString();
        LeaveOpacityValue.Text = config.Panel.OpacityLeave.ToString();
        RefreshElements();

        ViewIcons.IsChecked = config.Menu.ViewMode == "Icons";
        ViewList.IsChecked = config.Menu.ViewMode is "List" or "";
        ViewTree.IsChecked = config.Menu.ViewMode == "Tree";
        GenericBox.IsChecked = config.Menu.ShowGenericNames;
        CategoryNamesBox.IsChecked = config.Menu.ShowCategoryNames;
        TooltipsBox.IsChecked = config.Menu.ShowTooltips;
        DescriptionsBox.IsChecked = config.Menu.ShowDescriptions;
        SelectTag(AppIconBox, config.Menu.AppIconSize);
        SelectTag(CategoryIconBox, config.Menu.CategoryIconSize);
        MenuWidthBox.Text = config.Menu.MenuWidth.ToString();
        MenuHeightBox.Text = config.Menu.MenuHeight.ToString();
        OpacitySlider.Value = config.Menu.BackgroundOpacity;
        OpacityValue.Text = config.Menu.BackgroundOpacity.ToString();

        HorizontalBox.IsChecked = config.Menu.CategoriesHorizontal;
        CategoriesLeftBox.IsChecked = config.Menu.CategoriesOnLeft;
        ProfileBottomBox.IsChecked = config.Menu.ProfileOnBottom;
        SearchBottomBox.IsChecked = config.Menu.SearchOnBottom;
        CommandsSearchBox.IsChecked = config.Menu.CommandsNextToSearch;
        SelectTag(ProfileBox, config.Menu.ProfileStyle);
        SelectTag(DisplayBox, config.Menu.ButtonDisplay);
        TitleBox.Text = config.Menu.ButtonTitle;
        SingleRowBox.IsChecked = config.Menu.SinglePanelRow;
        ShowIcon();

        FavRadio.IsChecked = config.Menu.DefaultCategory == "Favorites";
        RecentRadio.IsChecked = config.Menu.DefaultCategory == "Recent";
        AllRadio.IsChecked = config.Menu.DefaultCategory == "All";
        HoverBox.IsChecked = config.Menu.HoverSwitch;
        StayBox.IsChecked = config.Menu.StayVisibleOnFocusLoss;
        SortBox.IsChecked = config.Menu.SortCategories;
        AltF1Box.IsChecked = config.Menu.AltF1;
        RecentCountBox.Text = config.Menu.RecentCount.ToString();
        IncludeFavBox.IsChecked = config.Menu.RecentIncludeFavorites;
        ConfirmBox.IsChecked = config.Menu.ConfirmSessionCommands;
        SearchDescBox.IsChecked = config.Menu.SearchDescriptions;
        SearchExeBox.IsChecked = config.Menu.SearchExecutables;
        RefreshCommands();
        _loading = false;
    }

    void FillMonitors()
    {
        MonitorBox.Items.Clear();
        foreach (var mon in Win32.GetMonitors())
        {
            string label = (mon.Primary ? "Primary  " : "Display  ") + mon.Device;
            MonitorBox.Items.Add(new ComboBoxItem { Content = label, Tag = mon.Device });
        }
    }

    static void FillIconSizes(ComboBox box)
    {
        foreach (var size in new[] { "Smaller", "Small", "Medium", "Large", "Larger" })
            box.Items.Add(new ComboBoxItem { Content = size, Tag = size });
    }

    void RefreshElements()
    {
        bool loading = _loading;
        _loading = true;
        string? id = _keepElementId ?? (ElementList.SelectedItem as PanelItem)?.Id;
        ElementList.ItemsSource = null;
        ElementList.ItemsSource = SettingsStore.Instance.Config.Panel.Items.ToList();
        foreach (PanelItem item in ElementList.Items)
        {
            if (item.Id == id)
            {
                ElementList.SelectedItem = item;
                break;
            }
        }
        _keepElementId = null;
        _loading = loading;
        UpdateEditButton();
    }

    void RefreshCommands()
    {
        bool loading = _loading;
        _loading = true;
        var selected = CommandList.SelectedItem as CommandItem;
        CommandList.Items.Clear();
        foreach (var cmd in SettingsStore.Instance.Config.Menu.Commands)
            CommandList.Items.Add(cmd);
        CommandList.DisplayMemberPath = "Name";
        if (selected != null)
            CommandList.SelectedItem = SettingsStore.Instance.Config.Menu.Commands.FirstOrDefault(c => c.Id == selected.Id);
        if (CommandList.SelectedIndex < 0 && CommandList.Items.Count > 0)
            CommandList.SelectedIndex = 0;
        _loading = loading;
        LoadCommandEditor();
    }

    void ShowPanelSub(string page)
    {
        PanelPageDisplay.Visibility = page == "display" ? Visibility.Visible : Visibility.Collapsed;
        PanelPageLook.Visibility = page == "look" ? Visibility.Visible : Visibility.Collapsed;
        PanelPageItems.Visibility = page == "items" ? Visibility.Visible : Visibility.Collapsed;
        PanelTabDisplay.IsChecked = page == "display";
        PanelTabLook.IsChecked = page == "look";
        PanelTabItems.IsChecked = page == "items";
    }

    void PanelSub_Click(object sender, RoutedEventArgs e) => ShowPanelSub((string)((FrameworkElement)sender).Tag);

    void FillProfiles()
    {
        bool loading = _loading;
        _loading = true;
        ProfileBoxPanel.Items.Clear();
        foreach (var profile in SettingsStore.Instance.Config.Profiles)
            ProfileBoxPanel.Items.Add(new ComboBoxItem { Content = profile.Name, Tag = profile.Name });
        SelectTag(ProfileBoxPanel, SettingsStore.Instance.Config.ActiveProfile);
        _loading = loading;
    }

    void SaveGeometry()
    {
        if (_loading) return;
        SettingsStore.Instance.Update(c =>
        {
            c.Panel.Mode = string.IsNullOrWhiteSpace(TagOf(ModeBox)) ? c.Panel.Mode : TagOf(ModeBox);
            c.Panel.Edge = string.IsNullOrWhiteSpace(TagOf(EdgeBox)) ? c.Panel.Edge : TagOf(EdgeBox);
            c.Panel.MonitorDevice = TagOf(MonitorBox);
            c.Panel.Locked = LockBox.IsChecked == true;
            c.Panel.AutoHideMode = string.IsNullOrWhiteSpace(TagOf(HideBox)) ? c.Panel.AutoHideMode : TagOf(HideBox);
            c.Panel.ReserveSpace = ReserveBox.IsChecked == true && c.Panel.AutoHideMode == "Never";
            c.Panel.Height = ReadInt(RowSizeBox.Text, c.Panel.Height, 16, 128);
            c.Panel.Rows = ReadInt(RowsBox.Text, c.Panel.Rows, 1, 6);
            c.Panel.BorderWidth = ReadInt(BorderBox.Text, c.Panel.BorderWidth, 0, 20);
            c.Panel.Length = (int)LengthSlider.Value;
            c.Panel.AutoLength = AutoLengthBox.IsChecked == true;
        }, ConfigArea.PanelGeometry);
        _loading = true;
        ReserveBox.IsChecked = SettingsStore.Instance.Config.Panel.ReserveSpace;
        LengthSlider.IsEnabled = AutoLengthBox.IsChecked != true;
        _loading = false;
    }

    void SaveChrome()
    {
        if (_loading) return;
        SettingsStore.Instance.Update(c =>
        {
            c.Panel.DarkMode = DarkBox.IsChecked == true;
            c.Panel.BackgroundStyle = string.IsNullOrWhiteSpace(TagOf(BackgroundStyleBox)) ? c.Panel.BackgroundStyle : TagOf(BackgroundStyleBox);
            c.Panel.Accent = string.IsNullOrWhiteSpace(TagOf(AccentBox)) ? c.Panel.Accent : TagOf(AccentBox);
            c.Panel.AutoIconSize = AutoIconBox.IsChecked == true;
            c.Panel.OpacityEnter = (int)EnterOpacitySlider.Value;
            c.Panel.OpacityLeave = (int)LeaveOpacitySlider.Value;
        }, ConfigArea.PanelChrome);
    }

    void PaintSwatch(string hex)
    {
        ColorSwatch.Background = Ui.Brush(hex, "#171923");
    }

    void SaveMenu()
    {
        if (_loading) return;
        SettingsStore.Instance.Update(c =>
        {
            c.Menu.ViewMode = ViewTree.IsChecked == true ? "Tree" : ViewIcons.IsChecked == true ? "Icons" : "List";
            c.Menu.ShowGenericNames = GenericBox.IsChecked == true;
            c.Menu.ShowCategoryNames = CategoryNamesBox.IsChecked == true;
            c.Menu.ShowTooltips = TooltipsBox.IsChecked == true;
            c.Menu.ShowDescriptions = DescriptionsBox.IsChecked == true;
            c.Menu.AppIconSize = TagOf(AppIconBox);
            c.Menu.CategoryIconSize = TagOf(CategoryIconBox);
            c.Menu.MenuWidth = ReadInt(MenuWidthBox.Text, c.Menu.MenuWidth, 360, 1600);
            c.Menu.MenuHeight = ReadInt(MenuHeightBox.Text, c.Menu.MenuHeight, 320, 1400);
            c.Menu.BackgroundOpacity = (int)OpacitySlider.Value;
            c.Menu.CategoriesHorizontal = HorizontalBox.IsChecked == true;
            c.Menu.CategoriesOnLeft = CategoriesLeftBox.IsChecked == true;
            c.Menu.ProfileOnBottom = ProfileBottomBox.IsChecked == true;
            c.Menu.SearchOnBottom = SearchBottomBox.IsChecked == true;
            c.Menu.CommandsNextToSearch = CommandsSearchBox.IsChecked == true;
            c.Menu.ProfileStyle = TagOf(ProfileBox);
            c.Menu.ButtonDisplay = TagOf(DisplayBox);
            c.Menu.ButtonTitle = TitleBox.Text;
            c.Menu.SinglePanelRow = SingleRowBox.IsChecked == true;
            c.Menu.DefaultCategory = RecentRadio.IsChecked == true ? "Recent" : AllRadio.IsChecked == true ? "All" : "Favorites";
            c.Menu.HoverSwitch = HoverBox.IsChecked == true;
            c.Menu.StayVisibleOnFocusLoss = StayBox.IsChecked == true;
            c.Menu.SortCategories = SortBox.IsChecked == true;
            c.Menu.AltF1 = AltF1Box.IsChecked == true;
            c.Menu.RecentCount = ReadInt(RecentCountBox.Text, c.Menu.RecentCount, 1, 30);
            c.Menu.RecentIncludeFavorites = IncludeFavBox.IsChecked == true;
            c.Menu.ConfirmSessionCommands = ConfirmBox.IsChecked == true;
            c.Menu.SearchDescriptions = SearchDescBox.IsChecked == true;
            c.Menu.SearchExecutables = SearchExeBox.IsChecked == true;
        }, ConfigArea.Menu);
    }

    void Geometry_Changed(object sender, SelectionChangedEventArgs e) => SaveGeometry();
    void Geometry_Click(object sender, RoutedEventArgs e) => SaveGeometry();
    void Chrome_Changed(object sender, SelectionChangedEventArgs e) => SaveChrome();
    void Chrome_Click(object sender, RoutedEventArgs e) => SaveChrome();

    void Mode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        string mode = TagOf(ModeBox);
        string edge = TagOf(EdgeBox);
        _loading = true;
        if (mode == "Horizontal" && edge is "Left" or "Right")
            SelectTag(EdgeBox, "Top");
        else if (mode is "Vertical" or "Deskbar" && edge is "Top" or "Bottom")
            SelectTag(EdgeBox, "Left");
        _loading = false;
        SaveGeometry();
    }

    void Hide_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (TagOf(HideBox) != "Never")
        {
            _loading = true;
            ReserveBox.IsChecked = false;
            _loading = false;
        }
        SaveGeometry();
    }

    void Reserve_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (ReserveBox.IsChecked == true)
        {
            _loading = true;
            SelectTag(HideBox, "Never");
            _loading = false;
        }
        SaveGeometry();
    }

    void Length_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || _loading || LengthValue == null) return;
        LengthValue.Text = ((int)LengthSlider.Value).ToString();
        SaveGeometry();
    }

    void PanelOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || _loading || EnterOpacityValue == null) return;
        EnterOpacityValue.Text = ((int)EnterOpacitySlider.Value).ToString();
        LeaveOpacityValue.Text = ((int)LeaveOpacitySlider.Value).ToString();
        SaveChrome();
    }

    void Measure_LostFocus(object sender, RoutedEventArgs e)
    {
        RowSizeBox.Text = ReadInt(RowSizeBox.Text, 34, 16, 128).ToString();
        RowsBox.Text = ReadInt(RowsBox.Text, 1, 1, 6).ToString();
        BorderBox.Text = ReadInt(BorderBox.Text, 0, 0, 20).ToString();
        SaveGeometry();
    }

    void RowDown(object sender, RoutedEventArgs e) => StepPanel(RowSizeBox, -1, 16, 128);
    void RowUp(object sender, RoutedEventArgs e) => StepPanel(RowSizeBox, 1, 16, 128);
    void RowsDown(object sender, RoutedEventArgs e) => StepPanel(RowsBox, -1, 1, 6);
    void RowsUp(object sender, RoutedEventArgs e) => StepPanel(RowsBox, 1, 1, 6);
    void BorderDown(object sender, RoutedEventArgs e) => StepPanel(BorderBox, -1, 0, 20);
    void BorderUp(object sender, RoutedEventArgs e) => StepPanel(BorderBox, 1, 0, 20);

    void StepPanel(TextBox box, int delta, int min, int max)
    {
        int value = Math.Clamp(ReadInt(box.Text, min, min, max) + delta, min, max);
        box.Text = value.ToString();
        SaveGeometry();
    }

    void Color_Click(object sender, RoutedEventArgs e)
    {
        var current = Ui.Parse(SettingsStore.Instance.Config.Panel.Background, System.Windows.Media.Color.FromRgb(0x17, 0x19, 0x23));
        using var dialog = new System.Windows.Forms.ColorDialog
        {
            Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B),
            FullOpen = true
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        string hex = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        PaintSwatch(hex);
        SettingsStore.Instance.Update(c =>
        {
            c.Panel.Background = hex;
            c.Panel.BackgroundStyle = "Solid";
        }, ConfigArea.PanelChrome);
        _loading = true;
        SelectTag(BackgroundStyleBox, "Solid");
        _loading = false;
    }

    void Profile_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        string name = TagOf(ProfileBoxPanel);
        if (string.IsNullOrWhiteSpace(name) || name == SettingsStore.Instance.Config.ActiveProfile) return;
        SettingsStore.Instance.SwitchProfile(name);
        LoadUi();
    }

    void ProfileAdd_Click(object sender, RoutedEventArgs e)
    {
        SettingsStore.Instance.AddProfile();
        LoadUi();
    }

    void ProfileRemove_Click(object sender, RoutedEventArgs e)
    {
        SettingsStore.Instance.RemoveActiveProfile();
        LoadUi();
    }

    void Backup_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        var save = new MenuItem { Header = "Backup…" };
        save.Click += (_, _) =>
        {
            var dlg = new SaveFileDialog { Filter = "Panel backup|*.json", FileName = "panel-backup.json" };
            if (dlg.ShowDialog(this) != true) return;
            File.WriteAllText(dlg.FileName, SettingsStore.Instance.ExportProfiles());
        };
        var restore = new MenuItem { Header = "Restore…" };
        restore.Click += (_, _) =>
        {
            var dlg = new OpenFileDialog { Filter = "Panel backup|*.json" };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                SettingsStore.Instance.ImportProfiles(File.ReadAllText(dlg.FileName));
                LoadUi();
            }
            catch (Exception ex)
            {
                Dialogs.Alert(this, ex.Message);
            }
        };
        menu.Items.Add(save);
        menu.Items.Add(restore);
        menu.PlacementTarget = (FrameworkElement)sender;
        menu.IsOpen = true;
    }

    void Startup_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool on = StartupBox.IsChecked == true;
        SettingsStore.Instance.Update(c => c.RunAtStartup = on, ConfigArea.PanelChrome);
        StartupService.Apply(on);
    }

    void Menu_Click(object sender, RoutedEventArgs e) => SaveMenu();
    void Menu_Changed(object sender, SelectionChangedEventArgs e) => SaveMenu();

    void Opacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || _loading) return;
        OpacityValue.Text = ((int)OpacitySlider.Value).ToString();
        SaveMenu();
    }

    void View_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _loading = true;
        ViewIcons.IsChecked = sender == ViewIcons;
        ViewList.IsChecked = sender == ViewList;
        ViewTree.IsChecked = sender == ViewTree;
        _loading = false;
        SaveMenu();
    }

    void Size_LostFocus(object sender, RoutedEventArgs e)
    {
        MenuWidthBox.Text = ReadInt(MenuWidthBox.Text, 570, 360, 1600).ToString();
        MenuHeightBox.Text = ReadInt(MenuHeightBox.Text, 700, 320, 1400).ToString();
        RecentCountBox.Text = ReadInt(RecentCountBox.Text, 10, 1, 30).ToString();
        SaveMenu();
    }

    void WidthDown(object sender, RoutedEventArgs e) => Step(MenuWidthBox, -10, 360, 1600);
    void WidthUp(object sender, RoutedEventArgs e) => Step(MenuWidthBox, 10, 360, 1600);
    void HeightDown(object sender, RoutedEventArgs e) => Step(MenuHeightBox, -10, 320, 1400);
    void HeightUp(object sender, RoutedEventArgs e) => Step(MenuHeightBox, 10, 320, 1400);
    void RecentDown(object sender, RoutedEventArgs e) => Step(RecentCountBox, -1, 1, 30);
    void RecentUp(object sender, RoutedEventArgs e) => Step(RecentCountBox, 1, 1, 30);

    void Step(TextBox box, int delta, int min, int max)
    {
        int value = Math.Clamp(ReadInt(box.Text, min, min, max) + delta, min, max);
        box.Text = value.ToString();
        SaveMenu();
    }

    void ShowIcon()
    {
        IconPreview.Child = Ui.IconOrMark(SettingsStore.Instance.Config.Menu.ButtonIconPath, 36);
    }

    void BrowseIcon_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Icons and images|*.png;*.jpg;*.jpeg;*.bmp;*.ico;*.exe;*.dll|All files|*.*" };
        if (dlg.ShowDialog(this) != true) return;
        SettingsStore.Instance.Update(c => c.Menu.ButtonIconPath = dlg.FileName, ConfigArea.Menu);
        ShowIcon();
    }

    void ClearIcon_Click(object sender, RoutedEventArgs e)
    {
        SettingsStore.Instance.Update(c => c.Menu.ButtonIconPath = "", ConfigArea.Menu);
        ShowIcon();
    }

    void Element_Selected(object sender, SelectionChangedEventArgs e) => UpdateEditButton();

    void UpdateEditButton() => EditElementButton.IsEnabled = ElementList.SelectedItem is PanelItem { Kind: PanelKinds.Launcher };

    Point? _press;
    PanelItem? _dragItem;

    void Add_Click(object sender, RoutedEventArgs e)
    {
        string? afterId = (ElementList.SelectedItem as PanelItem)?.Id;
        Dialogs.AddPanelItems(this, kind =>
        {
            PanelItem item;
            if (kind == PanelKinds.Launcher)
            {
                var app = Dialogs.Pick(this);
                if (app == null) return;
                item = new PanelItem
                {
                    Kind = PanelKinds.Launcher,
                    Label = app.Name,
                    Target = !string.IsNullOrWhiteSpace(app.TargetPath) ? app.TargetPath :
                        !string.IsNullOrWhiteSpace(app.AppUserModelId) ? "aumid:" + app.AppUserModelId : app.Key,
                    Arguments = app.Arguments,
                    WorkingDirectory = app.WorkingDirectory,
                    IconPath = string.IsNullOrWhiteSpace(app.IconPath) ? app.TargetPath : app.IconPath,
                    IconIndex = app.IconIndex
                };
            }
            else
            {
                item = new PanelItem { Kind = kind };
            }
            InsertAfter(item, afterId);
            afterId = item.Id;
        });
    }

    void InsertAfter(PanelItem item, string? afterId)
    {
        _keepElementId = item.Id;
        SettingsStore.Instance.Update(c =>
        {
            var items = c.Panel.Items;
            int index = string.IsNullOrEmpty(afterId) ? -1 : items.FindIndex(i => i.Id == afterId);
            if (index < 0) items.Add(item);
            else items.Insert(index + 1, item);
        }, ConfigArea.PanelItems);
    }

    void Element_Press(object sender, MouseButtonEventArgs e)
    {
        _press = e.GetPosition(ElementList);
        _dragItem = ItemAt(_press.Value);
    }

    void Element_Drag(object sender, MouseEventArgs e)
    {
        if (_press == null || _dragItem == null || e.LeftButton != MouseButtonState.Pressed) return;
        var pos = e.GetPosition(ElementList);
        if (Math.Abs(pos.Y - _press.Value.Y) < 8) return;
        var item = _dragItem;
        _press = null;
        _dragItem = null;
        DragDrop.DoDragDrop(ElementList, new DataObject("WindowsWhisker.PanelItem", item.Id), DragDropEffects.Move);
    }

    void Element_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent("WindowsWhisker.PanelItem") ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    void Element_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData("WindowsWhisker.PanelItem") is not string id) return;
        var target = ItemAt(e.GetPosition(ElementList));
        if (target == null || target.Id == id) return;
        var items = SettingsStore.Instance.Config.Panel.Items;
        int from = items.FindIndex(i => i.Id == id);
        int to = items.FindIndex(i => i.Id == target.Id);
        if (from < 0 || to < 0 || from == to) return;
        var moved = items[from];
        items.RemoveAt(from);
        if (to > from) to--;
        items.Insert(to, moved);
        _keepElementId = id;
        SettingsStore.Instance.Update(_ => { }, ConfigArea.PanelItems);
        e.Handled = true;
    }

    PanelItem? ItemAt(Point point)
    {
        if (ElementList.InputHitTest(point) is not DependencyObject hit) return null;
        while (hit != null)
        {
            if (hit is ListBoxItem box && box.Content is PanelItem item) return item;
            hit = VisualTreeHelper.GetParent(hit);
        }
        return null;
    }

    void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (ElementList.SelectedItem is not PanelItem item || item.Kind != PanelKinds.Launcher) return;
        var app = Dialogs.Pick(this);
        if (app == null) return;
        item.Label = app.Name;
        item.Target = !string.IsNullOrWhiteSpace(app.TargetPath) ? app.TargetPath :
            !string.IsNullOrWhiteSpace(app.AppUserModelId) ? "aumid:" + app.AppUserModelId : app.Key;
        item.Arguments = app.Arguments;
        item.WorkingDirectory = app.WorkingDirectory;
        item.IconPath = string.IsNullOrWhiteSpace(app.IconPath) ? app.TargetPath : app.IconPath;
        item.IconIndex = app.IconIndex;
        _keepElementId = item.Id;
        SettingsStore.Instance.Update(_ => { }, ConfigArea.PanelItems);
    }

    void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (ElementList.SelectedItem is not PanelItem item) return;
        SettingsStore.Instance.Update(c => c.Panel.Items.RemoveAll(i => i.Id == item.Id), ConfigArea.PanelItems);
    }

    void Up_Click(object sender, RoutedEventArgs e) => MoveElement(-1);
    void Down_Click(object sender, RoutedEventArgs e) => MoveElement(1);

    void MoveElement(int delta)
    {
        if (ElementList.SelectedItem is not PanelItem item) return;
        var items = SettingsStore.Instance.Config.Panel.Items;
        int index = items.FindIndex(i => i.Id == item.Id);
        int next = index + delta;
        if (index < 0 || next < 0 || next >= items.Count) return;
        _keepElementId = item.Id;
        (items[index], items[next]) = (items[next], items[index]);
        SettingsStore.Instance.Update(_ => { }, ConfigArea.PanelItems);
    }

    void Command_Selected(object sender, SelectionChangedEventArgs e) => LoadCommandEditor();

    void LoadCommandEditor()
    {
        _loading = true;
        if (CommandList.SelectedItem is not CommandItem cmd)
        {
            CmdNameBox.Text = "";
            CmdLineBox.Text = "";
            CmdVisibleBox.IsChecked = false;
            CmdConfirmBox.IsChecked = false;
            CmdLineBox.IsEnabled = false;
            _loading = false;
            return;
        }
        CmdNameBox.Text = cmd.Name;
        CmdLineBox.Text = cmd.CommandLine;
        CmdVisibleBox.IsChecked = cmd.Visible;
        CmdConfirmBox.IsChecked = cmd.Confirm;
        CmdLineBox.IsEnabled = cmd.Kind == "command";
        _loading = false;
    }

    void Command_Edited(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (CommandList.SelectedItem is not CommandItem cmd) return;
        cmd.Name = CmdNameBox.Text;
        cmd.CommandLine = CmdLineBox.Text;
        cmd.Visible = CmdVisibleBox.IsChecked == true;
        cmd.Confirm = CmdConfirmBox.IsChecked == true;
        int index = CommandList.SelectedIndex;
        SettingsStore.Instance.Update(_ => { }, ConfigArea.Menu);
        _loading = true;
        CommandList.Items.Refresh();
        CommandList.SelectedIndex = index;
        _loading = false;
    }

    void CommandAdd_Click(object sender, RoutedEventArgs e)
    {
        var cmd = new CommandItem { Name = "Custom command", Kind = "command", CommandLine = "", Confirm = true, Visible = true };
        SettingsStore.Instance.Update(c => c.Menu.Commands.Add(cmd), ConfigArea.Menu);
        RefreshCommands();
        CommandList.SelectedItem = cmd;
    }

    void CommandRemove_Click(object sender, RoutedEventArgs e)
    {
        if (CommandList.SelectedItem is not CommandItem cmd) return;
        SettingsStore.Instance.Update(c => c.Menu.Commands.RemoveAll(i => i.Id == cmd.Id), ConfigArea.Menu);
        RefreshCommands();
    }

    void Help_Click(object sender, RoutedEventArgs e) => Dialogs.Help(this);
    void Close_Click(object sender, RoutedEventArgs e) => Close();

    static void SelectTag(ComboBox box, string tag)
    {
        foreach (var entry in box.Items)
        {
            if (entry is ComboBoxItem item && item.Tag is string value && value == tag)
            {
                box.SelectedItem = item;
                return;
            }
        }
    }

    static void SelectColor(ComboBox box, string hex)
    {
        foreach (var entry in box.Items)
        {
            if (entry is ComboBoxItem item && item.Tag is string value &&
                string.Equals(value, hex, StringComparison.OrdinalIgnoreCase))
            {
                box.SelectedItem = item;
                return;
            }
        }
        var custom = new ComboBoxItem { Content = "Custom " + hex, Tag = hex };
        box.Items.Add(custom);
        box.SelectedItem = custom;
    }

    static string TagOf(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

    static int ReadInt(string text, int fallback, int min, int max)
    {
        if (!int.TryParse(text, out int value)) value = fallback;
        return Math.Clamp(value, min, max);
    }
}
