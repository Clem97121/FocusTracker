using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FocusTracker.App.Controls
{
    public partial class XpPopup : UserControl
    {
        public XpPopup()
        {
            InitializeComponent();
        }

        public void Show(int xp)
        {
            XpText.Text = $"+{xp} XP";
            Visibility = Visibility.Visible;

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
            var moveUp = new DoubleAnimation(20, 0, TimeSpan.FromMilliseconds(300));
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(500))
            {
                BeginTime = TimeSpan.FromSeconds(3) // 🟢 было 2 — стало 3 секунды ожидания перед исчезновением
            };


            fadeOut.Completed += (s, e) => Visibility = Visibility.Collapsed;

            XpText.BeginAnimation(OpacityProperty, fadeIn);
            Root.BeginAnimation(OpacityProperty, fadeIn);

            if (Root.RenderTransform is TranslateTransform transform)
            {
                transform.BeginAnimation(TranslateTransform.YProperty, moveUp);
            }

            Root.BeginAnimation(OpacityProperty, fadeOut);
        }

    }
}
