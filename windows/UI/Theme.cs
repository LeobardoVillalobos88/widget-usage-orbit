using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace UsageOrbit.UI
{
    internal static class Theme
    {
        public static Button Button(string text, string tooltip, bool icon)
        {
            var button = new Button { Content = text, ToolTip = tooltip, Height = 22, MinWidth = 22,
                Padding = new Thickness(icon ? 0 : 6, 0, icon ? 0 : 6, 0), BorderThickness = new Thickness(0),
                Background = Brushes.Transparent, Foreground = Palette.Brush("#F2F4F8"), FontSize = icon ? 12 : 10 };
            if (icon) button.FontFamily = new FontFamily("Segoe MDL2 Assets");
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "surface";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            border.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            content.SetBinding(FrameworkElement.MarginProperty, new Binding("Padding") { RelativeSource = RelativeSource.TemplatedParent });
            border.AppendChild(content);
            var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, Palette.Brush("#334B5368"), "surface"));
            template.Triggers.Add(hover);
            var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focus.Setters.Add(new Setter(Border.BackgroundProperty, Palette.Brush("#66556688"), "surface"));
            template.Triggers.Add(focus);
            button.Template = template;
            System.Windows.Automation.AutomationProperties.SetName(button, tooltip);
            return button;
        }
        public static ContextMenu Menu()
        {
            var menu = new ContextMenu { Background = Palette.Brush("#24262D"), Foreground = Palette.Brush("#F2F4F8"), BorderBrush = Palette.Brush("#444855"), FontFamily = new FontFamily("Segoe UI"), FontSize = 12 };
            menu.Resources[SystemColors.MenuBrushKey] = menu.Background;
            menu.Resources[SystemColors.MenuTextBrushKey] = menu.Foreground;
            menu.Resources[SystemColors.WindowBrushKey] = menu.Background;
            menu.Resources[SystemColors.HighlightBrushKey] = Palette.Brush("#41495D");
            menu.Resources[SystemColors.HighlightTextBrushKey] = menu.Foreground;
            var style = new Style(typeof(MenuItem));
            style.Setters.Add(new Setter(Control.ForegroundProperty, menu.Foreground));
            style.Setters.Add(new Setter(Control.BackgroundProperty, menu.Background));
            var root = new FrameworkElementFactory(typeof(Grid));
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "highlight";
            border.SetValue(Border.PaddingProperty, new Thickness(9, 6, 9, 6));
            var row = new FrameworkElementFactory(typeof(DockPanel));
            var check = new FrameworkElementFactory(typeof(TextBlock));
            check.Name = "check";
            check.SetValue(TextBlock.TextProperty, "✓");
            check.SetValue(FrameworkElement.WidthProperty, 20.0);
            check.SetValue(UIElement.VisibilityProperty, Visibility.Hidden);
            row.AppendChild(check);
            var arrow = new FrameworkElementFactory(typeof(TextBlock));
            arrow.Name = "arrow";
            arrow.SetValue(TextBlock.TextProperty, "›");
            arrow.SetValue(DockPanel.DockProperty, Dock.Right);
            arrow.SetValue(FrameworkElement.MarginProperty, new Thickness(14, 0, 0, 0));
            arrow.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            row.AppendChild(arrow);
            var label = new FrameworkElementFactory(typeof(ContentPresenter));
            label.SetValue(ContentPresenter.ContentSourceProperty, "Header");
            row.AppendChild(label); border.AppendChild(row); root.AppendChild(border);
            var popup = new FrameworkElementFactory(typeof(Popup));
            popup.Name = "PART_Popup";
            popup.SetValue(Popup.PlacementProperty, PlacementMode.Right);
            popup.SetValue(Popup.AllowsTransparencyProperty, true);
            popup.SetValue(Popup.FocusableProperty, false);
            popup.SetBinding(Popup.IsOpenProperty, new Binding("IsSubmenuOpen") { RelativeSource = RelativeSource.TemplatedParent });
            var popupBorder = new FrameworkElementFactory(typeof(Border));
            popupBorder.SetValue(Border.BackgroundProperty, menu.Background);
            popupBorder.SetValue(Border.BorderBrushProperty, menu.BorderBrush);
            popupBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            popupBorder.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
            popup.AppendChild(popupBorder); root.AppendChild(popup);
            var template = new ControlTemplate(typeof(MenuItem)) { VisualTree = root };
            var highlighted = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
            highlighted.Setters.Add(new Setter(Border.BackgroundProperty, Palette.Brush("#41495D"), "highlight"));
            template.Triggers.Add(highlighted);
            var checkedTrigger = new Trigger { Property = MenuItem.IsCheckedProperty, Value = true };
            checkedTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "check"));
            template.Triggers.Add(checkedTrigger);
            var children = new Trigger { Property = ItemsControl.HasItemsProperty, Value = true };
            children.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "arrow"));
            template.Triggers.Add(children);
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.4));
            template.Triggers.Add(disabled);
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            menu.Resources[typeof(MenuItem)] = style;
            // La plantilla predeterminada dibuja una franja clara a la izquierda de las opciones.
            var frame = new FrameworkElementFactory(typeof(Border));
            frame.SetValue(Border.BackgroundProperty, menu.Background);
            frame.SetValue(Border.BorderBrushProperty, menu.BorderBrush);
            frame.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            frame.SetValue(Border.PaddingProperty, new Thickness(2));
            frame.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
            menu.Template = new ControlTemplate(typeof(ContextMenu)) { VisualTree = frame };
            return menu;
        }
    }
}
