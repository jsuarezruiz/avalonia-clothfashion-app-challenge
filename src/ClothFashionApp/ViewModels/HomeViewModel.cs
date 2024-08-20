using ClothFashionApp.Models;
using ClothFashionApp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Linq;

namespace ClothFashionApp.ViewModels
{
    public partial class HomeViewModel : ViewModelBase
    {
        Promotion _promotion;
        ObservableCollection<Category> _categories;
        ObservableCollection<Product> _popularProducts;

        public HomeViewModel()
        {
            LoadData();
        }

        public Promotion Promotion
        {
            get { return _promotion; }
            set
            {
                _promotion = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<Category> Categories
        {
            get { return _categories; }
            set
            {
                _categories = value;
                OnPropertyChanged();
            }
        }

        [ObservableProperty]
        Category? selectedCategory;

        public ObservableCollection<Product> PopularProducts
        {
            get { return _popularProducts; }
            set
            {
                _popularProducts = value;
                OnPropertyChanged();
            }
        }

        [ObservableProperty]
        Product selectedPopularProduct; 
        
        void LoadData()
        {
            Promotion = ClothFashionService.GetPromotion();
            Categories = new ObservableCollection<Category>(ClothFashionService.GetCategories());
            SelectedCategory = Categories.FirstOrDefault();
            PopularProducts = new ObservableCollection<Product>(ClothFashionService.GetPopularProducts());
        }
    }
}
