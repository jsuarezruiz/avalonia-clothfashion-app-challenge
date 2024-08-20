using Avalonia.Controls;
using ClothFashionApp.Controls;

namespace ClothFashionApp.Views
{
    public partial class HomeView : UserControl
    {
        public HomeView()
        {
            InitializeComponent();
        }

        protected override void OnSizeChanged(SizeChangedEventArgs e)
        {
            base.OnSizeChanged(e);

            const double ItemMargin = 12;

            PopularProductsVirtualizedGrid.ItemWidth = e.NewSize.Width / 2 - (ItemMargin * 2);
        }
    }
}