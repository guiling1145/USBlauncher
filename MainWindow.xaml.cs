using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using System.Text.Json;
using Fluent;
using WpfApp.Models;
using WpfApp.Services;

namespace WpfApp;

public partial class MainWindow : RibbonWindow
{
    private const string IconDir = "icons";
    private readonly AppManager appManager = new();
    private List<AppShortcut> appShortcuts = new();
    private int currentPage = 1;
    private int pageSize = 12; // 每页显示12个应用
    private bool showHidden = false;

    private System.Windows.Controls.ContextMenu? blankContextMenu;
    private System.Windows.Controls.ContextMenu? appContextMenu;

    public class ExeItem
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public BitmapSource? Icon { get; set; }
        public bool IsHidden { get; set; }
    }

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        Unloaded += MainWindow_Unloaded;
        InitializeContextMenus();
    }

    private void InitializeContextMenus()
    {
        // 创建应用项的右键菜单
        appContextMenu = new System.Windows.Controls.ContextMenu();
        appContextMenu.Items.Add(new System.Windows.Controls.MenuItem { Header = "打开", InputGestureText = "Enter" });
        appContextMenu.Items.Add(new System.Windows.Controls.MenuItem { Header = "以管理员身份运行" });
        appContextMenu.Items.Add(new System.Windows.Controls.Separator());
        appContextMenu.Items.Add(new System.Windows.Controls.MenuItem { Header = "打开文件位置" });
        appContextMenu.Items.Add(new System.Windows.Controls.MenuItem { Header = "复制路径" });
        appContextMenu.Items.Add(new System.Windows.Controls.MenuItem { Header = "全部复制路径" });
        appContextMenu.Items.Add(new System.Windows.Controls.Separator());
        appContextMenu.Items.Add(new System.Windows.Controls.MenuItem { Header = "隐藏" });
        appContextMenu.Items.Add(new System.Windows.Controls.MenuItem { Header = "删除", InputGestureText = "Del" });

        // 绑定事件处理
        ((System.Windows.Controls.MenuItem)appContextMenu.Items[0]).Click += OpenMenu_Click;
        ((System.Windows.Controls.MenuItem)appContextMenu.Items[1]).Click += RunAsAdmin_Click;
        ((System.Windows.Controls.MenuItem)appContextMenu.Items[3]).Click += OpenFolder_Click;
        ((System.Windows.Controls.MenuItem)appContextMenu.Items[4]).Click += CopyPath_Click;
        ((System.Windows.Controls.MenuItem)appContextMenu.Items[5]).Click += CopyAllPath_Click;
        ((System.Windows.Controls.MenuItem)appContextMenu.Items[7]).Click += HideOrShowMenuItem_Click;
        ((System.Windows.Controls.MenuItem)appContextMenu.Items[8]).Click += DeleteMenu_Click;

        // 创建空白区域的右键菜单
        blankContextMenu = new System.Windows.Controls.ContextMenu();
        var showHideItem = new System.Windows.Controls.MenuItem
        {
            Header = showHidden ? "隐藏隐藏应用" : "显示隐藏应用"
        };
        showHideItem.Click += (s, args) => ShowHiddenButton_Click(s, args);
        blankContextMenu.Items.Add(showHideItem);
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(IconDir);
        appShortcuts = appManager.LoadShortcuts();
        RefreshListView();
    }

    private void MainWindow_Unloaded(object sender, RoutedEventArgs e)
    {
        // 无需处理U盘事件
    }

    private Task SaveAppShortcutsAsync()
    {
        appManager.SaveShortcuts(appShortcuts);
        return Task.CompletedTask;
    }

    private void RefreshListView()
    {
        var filtered = appShortcuts
            .Where(a => showHidden || !a.IsHidden)
            .ToList();
        int total = filtered.Count;
        int totalPages = (total + pageSize - 1) / pageSize;
        if (currentPage > totalPages) currentPage = totalPages == 0 ? 1 : totalPages;
        if (currentPage < 1) currentPage = 1;
        var pageItems = filtered.Skip((currentPage - 1) * pageSize).Take(pageSize)
            .Select(a => new ExeItem
            {
                Name = a.Name,
                Path = a.Path,
                Icon = LoadAppIcon(a.IconFile, File.Exists(a.Path)),
                IsHidden = a.IsHidden
            }).ToList();
        ExeListView.ItemsSource = pageItems;
        // 修正：EmptyHint 需在 XAML 中有 x:Name="EmptyHint"
        EmptyHint.Visibility = pageItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private BitmapSource? LoadAppIcon(string iconFile, bool fileExists)
    {
        try
        {
            var iconPath = System.IO.Path.Combine(IconDir, iconFile);
            if (!File.Exists(iconPath)) return null;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(System.IO.Path.GetFullPath(iconPath));
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            if (!fileExists)
            {
                // 灰度处理
                var format = new FormatConvertedBitmap(bitmap, PixelFormats.Gray8, null, 0);
                return format;
            }
            return bitmap;
        }
        catch { return null; }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshListView();
    }

    private BitmapSource? GetExeIcon(string path)
    {
        try
        {
            using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(path))
            {
                if (icon != null)
                {
                    var bitmap = icon.ToBitmap();
                    var hBitmap = bitmap.GetHbitmap();
                    try
                    {
                        var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                            hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
                        return source;
                    }
                    finally
                    {
                        NativeMethods.DeleteObject(hBitmap);
                        bitmap.Dispose();
                    }
                }
            }
        }
        catch { }
        return null;
    }

    private void ExeListView_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        OpenSelectedApp(false);
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ExeListView.SelectedItem is ExeItem item)
        {
            var folder = System.IO.Path.GetDirectoryName(item.Path);
            if (folder != null)
            {
                Process.Start("explorer.exe", $"\"{folder}\"");
            }
        }
    }

    private void CopyPath_Click(object sender, RoutedEventArgs e)
    {
        var items = ExeListView.SelectedItems.Cast<ExeItem>().ToList();
        if (items.Count == 0 && ExeListView.SelectedItem is ExeItem single)
            items.Add(single);
        if (items.Count > 0)
        {
            var sb = new StringBuilder();
            foreach (var item in items)
                sb.AppendLine(item.Path);
            Clipboard.SetText(sb.ToString().Trim());
        }
    }

    private void CopyAllPath_Click(object sender, RoutedEventArgs e)
    {
        var items = ExeListView.ItemsSource as IEnumerable<ExeItem>;
        if (items != null)
        {
            var sb = new StringBuilder();
            foreach (var item in items)
                sb.AppendLine(item.Path);
            Clipboard.SetText(sb.ToString().Trim());
        }
    }

    private void RunAsAdmin_Click(object sender, RoutedEventArgs e)
    {
        if (ExeListView.SelectedItem is ExeItem item)
        {
            try
            {
                var psi = new ProcessStartInfo(item.Path) { UseShellExecute = true, Verb = "runas" };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"启动失败: {ex.Message}");
            }
        }
    }

    private void DeleteMenu_Click(object sender, RoutedEventArgs e)
    {
        DeleteSelectedExe();
    }

    private void ExeListView_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ExeListView_MouseDoubleClick(sender, null);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            DeleteSelectedExe();
            e.Handled = true;
        }
        else if (e.Key == Key.Apps)
        {
            if (ExeListView.ContextMenu != null)
            {
                ExeListView.ContextMenu.PlacementTarget = ExeListView;
                ExeListView.ContextMenu.IsOpen = true;
            }
            e.Handled = true;
        }
    }

    private void DeleteSelectedExe()
    {
        var items = ExeListView.SelectedItems.Cast<ExeItem>().ToList();
        if (items.Count == 0) return;
        var msg = items.Count == 1
            ? $"确定要删除此快捷方式吗？\n{items[0].Path}"
            : $"确定要删除这{items.Count}个快捷方式吗？";
        if (MessageBox.Show(msg, "删除确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            foreach (var item in items)
            {
                var shortcut = appShortcuts.FirstOrDefault(a => a.Path == item.Path);
                if (shortcut != null)
                {
                    var iconPath = System.IO.Path.Combine(IconDir, shortcut.IconFile);
                    if (File.Exists(iconPath))
                        File.Delete(iconPath);
                    appShortcuts.Remove(shortcut);
                }
            }
            _ = SaveAppShortcutsAsync();
            RefreshListView();
        }
    }

    private void ExeListView_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        // 只在有选中项时才自动选中鼠标下的项，否则不做任何操作
        if (ExeListView.SelectedItem == null)
        {
            // 获取鼠标下的项
            var pos = Mouse.GetPosition(ExeListView);
            var hit = VisualTreeHelper.HitTest(ExeListView, pos);
            if (hit != null)
            {
                var dep = hit.VisualHit;
                while (dep != null && dep is not ListViewItem)
                    dep = VisualTreeHelper.GetParent(dep);
                if (dep is ListViewItem lvi)
                {
                    lvi.IsSelected = true;
                }
            }
        }
        // 如果没有选中项，不弹出菜单
        if (ExeListView.SelectedItem == null)
        {
            e.Handled = true;
            return;
        }
        // 允许XAML中的ContextMenu显示
        ExeListView.ContextMenu = null;
    }

    private void OpenSelectedApp(bool asAdmin)
    {
        if (ExeListView.SelectedItem is ExeItem item)
        {
            try
            {
                var psi = new ProcessStartInfo(item.Path)
                {
                    UseShellExecute = true
                };
                if (asAdmin)
                    psi.Verb = "runas";
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"启动失败: {ex.Message}");
            }
        }
    }

    private void SelectAllButton_Click(object sender, RoutedEventArgs e)
    {
        ExeListView.SelectAll();
    }

    private void OpenMenu_Click(object sender, RoutedEventArgs e)
    {
        if (ExeListView.SelectedItem is ExeItem item)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = item.Path,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"启动失败: {ex.Message}");
            }
        }
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择要添加的程序",
            Filter = "可执行文件 (*.exe;*.lnk)|*.exe;*.lnk",
            Multiselect = true
        };
        if (dlg.ShowDialog() == true)
        {
            foreach (var file in dlg.FileNames)
            {
                try
                {
                    var fileName = System.IO.Path.GetFileName(file);
                    var iconFile = Guid.NewGuid().ToString("N") + ".png";
                    var iconPath = System.IO.Path.Combine(IconDir, iconFile);
                    var icon = GetExeIcon(file);
                    if (icon != null)
                    {
                        using var fileStream = new FileStream(iconPath, FileMode.Create, FileAccess.Write);
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(icon));
                        encoder.Save(fileStream);
                    }
                    appShortcuts.Add(new AppShortcut
                    {
                        Name = System.IO.Path.GetFileNameWithoutExtension(file),
                        Path = file,
                        IconFile = iconFile
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"添加失败: {ex.Message}");
                }
            }
            _ = SaveAppShortcutsAsync();
            RefreshListView();
        }
    }

    private void HideOrShowMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ExeListView.SelectedItem is ExeItem item)
        {
            var shortcut = appShortcuts.FirstOrDefault(a => a.Path == item.Path);
            if (shortcut != null)
            {
                shortcut.IsHidden = !shortcut.IsHidden;
                _ = SaveAppShortcutsAsync();
                RefreshListView();
            }
        }
    }

    private void ShowHiddenButton_Click(object sender, RoutedEventArgs e)
    {
        showHidden = !showHidden;
        RefreshListView();
    }

    private void ExeListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(ExeListView);
        var hit = VisualTreeHelper.HitTest(ExeListView, pos);
        bool hitItem = false;

        if (hit != null)
        {
            var dep = hit.VisualHit;
            while (dep != null)
            {
                if (dep is ListViewItem)
                {
                    hitItem = true;
                    break;
                }
                dep = VisualTreeHelper.GetParent(dep);
            }
        }

        if (!hitItem)
        {
            // 点击空白区域，取消选择
            ExeListView.SelectedItems.Clear();
            e.Handled = true;
        }
    }

    private void ExeListView_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(ExeListView);
        var hit = VisualTreeHelper.HitTest(ExeListView, pos);
        bool hitItem = false;

        if (hit != null)
        {
            var dep = hit.VisualHit;
            while (dep != null)
            {
                if (dep is ListViewItem)
                {
                    hitItem = true;
                    break;
                }
                dep = VisualTreeHelper.GetParent(dep);
            }
        }

        e.Handled = true; // 总是处理右键事件

        if (!hitItem)
        {
            // 点击空白区域
            ExeListView.SelectedItems.Clear();

            // 确保菜单已初始化
            if (blankContextMenu == null)
            {
                InitializeContextMenus();
            }
            else
            {
                // 更新菜单项文本
                ((System.Windows.Controls.MenuItem)blankContextMenu.Items[0]).Header = 
                    showHidden ? "隐藏隐藏应用" : "显示隐藏应用";
            }

            blankContextMenu.PlacementTarget = ExeListView;
            blankContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            blankContextMenu.IsOpen = true;
        }
        else
        {
            // 点击应用项
            // 确保菜单已初始化
            if (appContextMenu == null)
            {
                InitializeContextMenus();
            }

            // 更新"隐藏"菜单项的文本
            if (ExeListView.SelectedItem is ExeItem item)
            {
                var hideMenuItem = (System.Windows.Controls.MenuItem)appContextMenu.Items[7];
                hideMenuItem.Header = item.IsHidden ? "显示" : "隐藏";
            }

            appContextMenu.PlacementTarget = ExeListView;
            appContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            appContextMenu.IsOpen = true;
        }
    }

    private void ListViewItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 右键时选中该项
        if (sender is ListViewItem item && !item.IsSelected)
            item.IsSelected = true;
    }

    private void ExeListContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        // 已废弃或无实际用途，可留空
    }

    internal static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);
    }
}