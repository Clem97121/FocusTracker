using FocusTracker.Data;
using FocusTracker.Domain.Interfaces;
using FocusTracker.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace FocusTracker.App.ViewModels
{
    public class TasksPageViewModel : INotifyPropertyChanged
    {
        private readonly ITaskItemService _taskService;
        private readonly ISkillService _skillService;
        private readonly ITrackedProgramService _programService;
        private readonly ITaskProgramService _taskProgramService;
        private readonly IAppUsageStatService _appUsageStatService;
        private readonly ITaskProgramUsageService _taskProgramUsageService;
        private readonly ITaskCompletionService _taskCompletionService;

        public ObservableCollection<TaskItem> Tasks { get; set; } = new();
        public ObservableCollection<Skill> AvailableSkills { get; set; } = new();
        public ObservableCollection<TrackedProgram> AvailablePrograms { get; set; } = new();
        public ObservableCollection<TrackedProgram> SelectedPrograms { get; set; } = new();
        public ObservableCollection<TaskItem> CompletedTasks { get; set; } = new();
        public ObservableCollection<TaskItem> FilteredCompletedTasks { get; set; } = new();

        private string _newTaskTitle;
        public string NewTaskTitle
        {
            get => _newTaskTitle;
            set { _newTaskTitle = value; OnPropertyChanged(); }
        }

        private string _newTaskDescription;
        public string NewTaskDescription
        {
            get => _newTaskDescription;
            set { _newTaskDescription = value; OnPropertyChanged(); }
        }

        private Skill _selectedSkill;
        public Skill SelectedSkill
        {
            get => _selectedSkill;
            set { _selectedSkill = value; OnPropertyChanged(); }
        }

        private int _newTaskDifficulty = 1;
        public int NewTaskDifficulty
        {
            get => _newTaskDifficulty;
            set { _newTaskDifficulty = value; OnPropertyChanged(); }
        }

        private int _newTaskEstimatedMinutes = 30;
        public int NewTaskEstimatedMinutes
        {
            get => _newTaskEstimatedMinutes;
            set { _newTaskEstimatedMinutes = value; OnPropertyChanged(); }
        }

        public bool IsEditing { get; private set; }
        private TaskItem _editingTask;

        public string AddButtonText => IsEditing ? "Оновити" : "Додати";

        private bool _showHistory;
        public bool ShowHistory
        {
            get => _showHistory;
            set { _showHistory = value; OnPropertyChanged(); }
        }

        private DateTime? _historyFilterDate;
        public DateTime? HistoryFilterDate
        {
            get => _historyFilterDate;
            set { _historyFilterDate = value; OnPropertyChanged(); UpdateFilteredHistory(); }
        }

        private Skill _historyFilterSkill;
        public Skill HistoryFilterSkill
        {
            get => _historyFilterSkill;
            set { _historyFilterSkill = value; OnPropertyChanged(); UpdateFilteredHistory(); }
        }

        private TrackedProgram _historyFilterProgram;
        public TrackedProgram HistoryFilterProgram
        {
            get => _historyFilterProgram;
            set { _historyFilterProgram = value; OnPropertyChanged(); UpdateFilteredHistory(); }
        }

        private bool _newTaskIsPassive;
        public bool NewTaskIsPassive
        {
            get => _newTaskIsPassive;
            set { _newTaskIsPassive = value; OnPropertyChanged(); }
        }
        public TasksPageViewModel(
            ITaskItemService taskService,
            ISkillService skillService,
            ITrackedProgramService programService,
            ITaskProgramService taskProgramService,
            IAppUsageStatService appUsageStatService,
            ITaskProgramUsageService taskProgramUsageService,
            ITaskCompletionService taskCompletionService)
        {
            _taskService = taskService;
            _skillService = skillService;
            _programService = programService;
            _taskProgramService = taskProgramService;
            _appUsageStatService = appUsageStatService;
            _taskProgramUsageService = taskProgramUsageService;
            _taskCompletionService = taskCompletionService;

            LoadSkills();
            LoadPrograms();
            LoadTasks();
            LoadCompletedTasks();
        }

        public void LoadSkills()
        {
            AvailableSkills.Clear();
            foreach (var skill in _skillService.GetAll())
                AvailableSkills.Add(skill);
        }

        public void LoadPrograms()
        {
            AvailablePrograms.Clear();
            foreach (var program in _programService.GetAll().Where(p => !p.IsHidden))
                AvailablePrograms.Add(program);
        }

        public void LoadTasks()
        {
            Tasks.Clear();
            var skills = _skillService.GetAll();
            var programs = _programService.GetAll();
            var allTasks = _taskService.GetAll().Where(t => !t.Completed).ToList();

            foreach (var task in allTasks)
            {
                task.Skill = skills.FirstOrDefault(s => s.Id == task.SkillId);
                task.Programs = _taskProgramService
                    .GetByTaskId(task.Id)
                    .Select(link => programs.FirstOrDefault(p => p.Id == link.ProgramId))
                    .Where(p => p != null)
                    .ToList();

                Tasks.Add(task);
            }
        }

        public void AddTask()
        {
            if (string.IsNullOrWhiteSpace(NewTaskTitle) || SelectedSkill == null)
                return;

            var task = new TaskItem
            {
                Title = NewTaskTitle,
                Description = NewTaskDescription ?? "",
                SkillId = SelectedSkill.Id,
                DateCreated = DateTime.Now,
                Completed = false,
                Difficulty = NewTaskDifficulty,
                EstimatedMinutes = NewTaskEstimatedMinutes,
                IsPassive = NewTaskIsPassive
            };

            var savedTask = _taskService.Add(task);
            var todayStats = GetTodayStats();
            foreach (var program in SelectedPrograms)
            {
                _taskProgramService.Add(new TaskProgram { TaskId = savedTask.Id, ProgramId = program.Id });
                AddBaseline(savedTask.Id, program, todayStats);
            }

            ResetForm();
            LoadTasks();
        }

        private List<AppUsageStat> GetTodayStats() =>
            // Task.Run: .Result на UI-потоці без нього може зависнути
            Task.Run(() => _appUsageStatService.GetStatsForTodayAsync()).GetAwaiter().GetResult();

        // Запам'ятовує, скільки часу в програмі вже було сьогодні на момент прив'язки до задачі,
        // щоб при завершенні рахувати лише приріст
        private void AddBaseline(int taskId, TrackedProgram program, List<AppUsageStat> todayStats)
        {
            var stat = todayStats.FirstOrDefault(s => s.AppName == program.Identifier);
            _taskProgramUsageService.AddOrUpdate(new TaskProgramUsage
            {
                TaskId = taskId,
                ProgramId = program.Id,
                CountedActiveSeconds = 0,
                CountedPassiveSeconds = 0,
                InitialActiveSeconds = (int)(stat?.ActiveTime.TotalSeconds ?? 0),
                InitialPassiveSeconds = (int)(stat?.TotalTime.TotalSeconds ?? 0),
                BaselineDate = DateTime.Today
            });
        }

        public void EditTask(TaskItem task)
        {
            _editingTask = task;
            IsEditing = true;
            NewTaskTitle = task.Title;
            NewTaskDescription = task.Description;
            SelectedSkill = AvailableSkills.FirstOrDefault(s => s.Id == task.SkillId);
            NewTaskDifficulty = task.Difficulty;
            NewTaskEstimatedMinutes = task.EstimatedMinutes;
            NewTaskIsPassive = task.IsPassive;
            SelectedPrograms.Clear();
            foreach (var p in task.Programs)
                SelectedPrograms.Add(p);
            OnPropertyChanged(nameof(AddButtonText));
        }

        public void SaveTaskChanges()
        {
            if (_editingTask == null) return;

            _editingTask.Title = NewTaskTitle;
            _editingTask.Description = NewTaskDescription;
            _editingTask.SkillId = SelectedSkill?.Id ?? _editingTask.SkillId;
            _editingTask.Difficulty = NewTaskDifficulty;
            _editingTask.EstimatedMinutes = NewTaskEstimatedMinutes;
            _editingTask.IsPassive = NewTaskIsPassive;

            _taskService.Update(_editingTask);

            var oldProgramIds = _taskProgramService.GetByTaskId(_editingTask.Id).Select(tp => tp.ProgramId).ToHashSet();
            var newProgramIds = SelectedPrograms.Select(p => p.Id).ToHashSet();

            // Прибрані програми: видаляємо і зв'язок, і базову точку
            foreach (var programId in oldProgramIds.Except(newProgramIds))
                _taskProgramUsageService.Delete(_editingTask.Id, programId);

            _taskProgramService.DeleteByTaskId(_editingTask.Id);
            foreach (var program in SelectedPrograms)
                _taskProgramService.Add(new TaskProgram { TaskId = _editingTask.Id, ProgramId = program.Id });

            // Нові програми рахуються з моменту додавання, а не за весь день
            var addedPrograms = SelectedPrograms.Where(p => !oldProgramIds.Contains(p.Id)).ToList();
            if (addedPrograms.Count > 0)
            {
                var todayStats = GetTodayStats();
                foreach (var program in addedPrograms)
                    AddBaseline(_editingTask.Id, program, todayStats);
            }

            ResetForm();
            LoadTasks();
        }

        public void DeleteTask(TaskItem task)
        {
            _taskProgramService.DeleteByTaskId(task.Id);
            _taskService.Delete(task.Id);
            LoadTasks();
        }

        public void CancelTaskEdit() => ResetForm();
        public void StartNewTask() => ResetForm();

        public Task<int> CompleteTask(TaskItem task)
        {
            if (task.Completed) return Task.FromResult(0);

            int earnedXp = _taskCompletionService.Complete(task.Id);
            LoadTasks();
            LoadCompletedTasks();
            return Task.FromResult(earnedXp);
        }

        private void ResetForm()
        {
            NewTaskTitle = "";
            NewTaskDescription = "";
            SelectedSkill = null;
            SelectedPrograms.Clear();
            NewTaskDifficulty = 1;
            NewTaskEstimatedMinutes = 30;
            NewTaskIsPassive = false;
            IsEditing = false;
            _editingTask = null;
            OnPropertyChanged(nameof(AddButtonText));
        }

        public void LoadCompletedTasks()
        {
            CompletedTasks.Clear();

            var skills = _skillService.GetAll().ToList();
            var programs = _programService.GetAll().ToList();

            // Забираємо всі завершені завдання, відсортувавши за датою створення зверху вниз
            var allTasks = _taskService.GetAll()
                                       .Where(t => t.Completed)
                                       .OrderByDescending(t => t.DateCreated)
                                       .ToList();

            foreach (var task in allTasks)
            {
                // Підтягуємо навичку
                task.Skill = skills.FirstOrDefault(s => s.Id == task.SkillId);

                // Підтягуємо програми
                task.Programs = _taskProgramService
                    .GetByTaskId(task.Id)
                    .Select(link => programs.FirstOrDefault(p => p.Id == link.ProgramId))
                    .Where(p => p != null)
                    .ToList();

                // Тепер просто беремо те, що вже записано в БД
                // (поле EarnedXp та ActiveSeconds вже мають значення, оскільки ми їх зберегли в CompleteTask)
                // Якщо по якимось завданням цих значень ще немає — можна підстановити 0
                task.EarnedXp = task.EarnedXp ?? 0;
                task.ActiveSeconds = task.ActiveSeconds ?? 0;

                CompletedTasks.Add(task);
            }

            UpdateFilteredHistory();
        }



        private void UpdateFilteredHistory()
        {
            FilteredCompletedTasks.Clear();

            var filtered = CompletedTasks.AsEnumerable();

            if (HistoryFilterDate.HasValue)
                filtered = filtered.Where(t => t.DateCreated.Date == HistoryFilterDate.Value.Date);

            if (HistoryFilterSkill != null)
                filtered = filtered.Where(t => t.SkillId == HistoryFilterSkill.Id);

            if (HistoryFilterProgram != null)
                filtered = filtered.Where(t => t.Programs.Any(p => p.Id == HistoryFilterProgram.Id));

            foreach (var task in filtered)
                FilteredCompletedTasks.Add(task);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
