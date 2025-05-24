using FocusTracker.Domain.Interfaces;
using FocusTracker.Domain.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Threading.Tasks;

namespace FocusTracker.App.ViewModels
{
    public class ProgramsViewModel : INotifyPropertyChanged
    {
        private readonly IProgramService _programService;

        public ObservableCollection<TrackedProgram> VisiblePrograms { get; } = new();
        public ObservableCollection<TrackedProgram> HiddenPrograms { get; } = new();

        private string _sortOption = "Name";
        public string SortOption
        {
            get => _sortOption;
            set
            {
                if (_sortOption != value)
                {
                    _sortOption = value;
                    OnPropertyChanged();
                    SortAndUpdateCollections();
                }
            }
        }

        private bool _showHidden = false;
        public bool ShowHidden
        {
            get => _showHidden;
            set
            {
                if (_showHidden != value)
                {
                    _showHidden = value;
                    OnPropertyChanged();
                }
            }
        }


        private List<TrackedProgram> _allPrograms = new();

        public ProgramsViewModel(IProgramService programService)
        {
            _programService = programService;
        }

        public async Task UpdateProgramAsync(TrackedProgram program)
        {
            await _programService.UpdateAsync(program);
        }

        public async Task LoadProgramsAsync()
        {
            VisiblePrograms.Clear();
            HiddenPrograms.Clear();

            _allPrograms = await _programService.GetAllAsync();

            // обновление DisplayName и IconBytes для всех программ, если нужно
            foreach (var program in _allPrograms)
            {
                // 👇 этот метод мы реализовали в TrackedProgramService
                await _programService.UpdateDisplayInfoIfNeededAsync(program);

                program.PropertyChanged += async (sender, e) =>
                {
                    if (e.PropertyName == nameof(TrackedProgram.IsHidden))
                    {
                        App.Current.Dispatcher.Invoke(SortAndUpdateCollections);
                    }

                    await _programService.UpdateAsync(program);
                };
            }

            SortAndUpdateCollections();
        }


        private void SortAndUpdateCollections()
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                VisiblePrograms.Clear();
                HiddenPrograms.Clear();

                IEnumerable<TrackedProgram> sorted;

                switch (SortOption)
                {
                    case "Category":
                        sorted = _allPrograms.OrderBy(p => p.Category);
                        break;
                    case "Tracked":
                        sorted = _allPrograms.OrderByDescending(p => p.IsTracked);
                        break;
                    default:
                        sorted = _allPrograms.OrderBy(p => p.DisplayName ?? p.Name);
                        break;
                }

                foreach (var p in sorted)
                {
                    if (p.IsHidden)
                        HiddenPrograms.Add(p);
                    else
                        VisiblePrograms.Add(p);
                }
            });
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
