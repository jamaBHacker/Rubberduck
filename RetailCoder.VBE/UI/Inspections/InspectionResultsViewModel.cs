﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Vbe.Interop;
using Rubberduck.Common;
using Rubberduck.Inspections;
using Rubberduck.Parsing.VBA;
using Rubberduck.Settings;
using Rubberduck.UI.Command;
using Rubberduck.UI.Command.MenuItems;
using Rubberduck.UI.Controls;
using Rubberduck.UI.Settings;
using Rubberduck.VBEditor.Extensions;
using NLog;

namespace Rubberduck.UI.Inspections
{
    public sealed class InspectionResultsViewModel : ViewModelBase, INavigateSelection, IDisposable
    {
        private readonly RubberduckParserState _state;
        private readonly IInspector _inspector;
        private readonly VBE _vbe;
        private readonly IClipboardWriter _clipboard;
        private readonly IGeneralConfigService _configService;
        private readonly IOperatingSystem _operatingSystem;
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public InspectionResultsViewModel(RubberduckParserState state, IInspector inspector, VBE vbe, INavigateCommand navigateCommand, IClipboardWriter clipboard, 
                                          IGeneralConfigService configService, IOperatingSystem operatingSystem)
        {
            _state = state;
            _inspector = inspector;
            _vbe = vbe;
            _navigateCommand = navigateCommand;
            _clipboard = clipboard;
            _configService = configService;
            _operatingSystem = operatingSystem;
            _refreshCommand = new DelegateCommand(async param => await Task.Run(() => ExecuteRefreshCommandAsync(param)), CanExecuteRefreshCommand);
            _disableInspectionCommand = new DelegateCommand(ExecuteDisableInspectionCommand);
            _quickFixCommand = new DelegateCommand(ExecuteQuickFixCommand, CanExecuteQuickFixCommand);
            _quickFixInModuleCommand = new DelegateCommand(ExecuteQuickFixInModuleCommand);
            _quickFixInProjectCommand = new DelegateCommand(ExecuteQuickFixInProjectCommand);
            _copyResultsCommand = new DelegateCommand(ExecuteCopyResultsCommand, CanExecuteCopyResultsCommand);
            _openSettingsCommand = new DelegateCommand(OpenSettings);

            _setInspectionTypeGroupingCommand = new DelegateCommand(param =>
            {
                GroupByInspectionType = (bool)param;
                GroupByLocation = !(bool)param;
            });

            _setLocationGroupingCommand = new DelegateCommand(param =>
            {
                GroupByLocation = (bool)param;
                GroupByInspectionType = !(bool)param;
            });

            _state.StateChanged += _state_StateChanged;
        }

        private ObservableCollection<ICodeInspectionResult> _results = new ObservableCollection<ICodeInspectionResult>();
        public ObservableCollection<ICodeInspectionResult> Results
        {
            get { return _results; }
            private set
            {
                _results = value;
                OnPropertyChanged();
            }
        }

        private CodeInspectionQuickFix _defaultFix;

        private INavigateSource _selectedItem;
        public INavigateSource SelectedItem
        {
            get { return _selectedItem; }
            set
            {
                _selectedItem = value; 
                OnPropertyChanged();

                SelectedInspection = null;
                CanQuickFix = false;
                CanExecuteQuickFixInModule = false;
                CanExecuteQuickFixInProject = false;

                var inspectionResult = _selectedItem as InspectionResultBase;
                if (inspectionResult != null)
                {
                    SelectedInspection = inspectionResult.Inspection;
                    CanQuickFix = inspectionResult.HasQuickFixes;
                    _defaultFix = inspectionResult.DefaultQuickFix;
                    CanExecuteQuickFixInModule = _defaultFix != null && _defaultFix.CanFixInModule;
                }

                CanDisableInspection = SelectedInspection != null;
                CanExecuteQuickFixInProject = _defaultFix != null && _defaultFix.CanFixInProject;
            }
        }

        private IInspection _selectedInspection;
        public IInspection SelectedInspection
        {
            get { return _selectedInspection; }
            set
            {
                _selectedInspection = value;
                OnPropertyChanged();
            }
        }

        private bool _groupByInspectionType = true;
        public bool GroupByInspectionType
        {
            get { return _groupByInspectionType; }
            set
            {
                if (_groupByInspectionType == value) { return; }
                
                _groupByInspectionType = value;
                OnPropertyChanged();
            }
        }

        private bool _groupByLocation;
        public bool GroupByLocation
        {
            get { return _groupByLocation; }
            set
            {
                if (_groupByLocation == value) { return; }
                
                _groupByLocation = value;
                OnPropertyChanged();
            }
        }

        private readonly ICommand _setInspectionTypeGroupingCommand;
        public ICommand SetInspectionTypeGroupingCommand { get { return _setInspectionTypeGroupingCommand; } }

        private readonly ICommand _setLocationGroupingCommand;
        public ICommand SetLocationGroupingCommand { get { return _setLocationGroupingCommand; } }

        private readonly INavigateCommand _navigateCommand;
        public INavigateCommand NavigateCommand { get { return _navigateCommand; } }

        private readonly ICommand _refreshCommand;
        public ICommand RefreshCommand { get { return _refreshCommand; } }

        private readonly ICommand _quickFixCommand;
        public ICommand QuickFixCommand { get { return _quickFixCommand; } }

        private readonly ICommand _quickFixInModuleCommand;
        public ICommand QuickFixInModuleCommand { get { return _quickFixInModuleCommand; } }

        private readonly ICommand _quickFixInProjectCommand;
        public ICommand QuickFixInProjectCommand { get { return _quickFixInProjectCommand; } }

        private readonly ICommand _disableInspectionCommand;
        public ICommand DisableInspectionCommand { get { return _disableInspectionCommand; } }

        private readonly ICommand _copyResultsCommand;
        public ICommand CopyResultsCommand { get { return _copyResultsCommand; } }

        private readonly ICommand _openSettingsCommand;
        public ICommand OpenTodoSettings { get { return _openSettingsCommand; } }

        private void OpenSettings(object param)
        {
            using (var window = new SettingsForm(_configService, _operatingSystem, SettingsViews.InspectionSettings))
            {
                window.ShowDialog();
            }
        }

        private bool _canRefresh = true;
        public bool CanRefresh
        {
            get { return _canRefresh; }
            private set
            {
                _canRefresh = value; 
                OnPropertyChanged();
            }
        }

        private bool _canQuickFix;
        public bool CanQuickFix { get { return _canQuickFix; } set { _canQuickFix = value; OnPropertyChanged(); } }

        private bool _isBusy;
        public bool IsBusy { get { return _isBusy; } set { _isBusy = value; OnPropertyChanged(); } }

        private async void ExecuteRefreshCommandAsync(object parameter)
        {
            CanRefresh = _vbe.HostApplication() != null && _state.IsDirty();
            if (!CanRefresh)
            {
                return;
            }
            await Task.Yield();

            IsBusy = true;

            _logger.Debug("InspectionResultsViewModel.ExecuteRefreshCommand - requesting reparse");
            _state.OnParseRequested(this);
        }

        private bool CanExecuteRefreshCommand(object parameter)
        {
            return !IsBusy && _state.IsDirty();
        }

        private async void _state_StateChanged(object sender, EventArgs e)
        {
            _logger.Debug("InspectionResultsViewModel handles StateChanged...");
            if (_state.Status != ParserState.Ready)
            {
                IsBusy = false;
                return;
            }

            _logger.Debug("Running code inspections...");
            IsBusy = true;

            var results = (await _inspector.FindIssuesAsync(_state, CancellationToken.None)).ToList();

            UiDispatcher.Invoke(() =>
            {
                Results = new ObservableCollection<ICodeInspectionResult>(results);

                CanRefresh = true;
                IsBusy = false;
                SelectedItem = null;
            });
        }

        private void ExecuteQuickFixes(IEnumerable<CodeInspectionQuickFix> quickFixes)
        {
            var fixes = quickFixes.ToList();
            var completed = 0;
            var cancelled = 0;
            foreach (var quickFix in fixes)
            {
                quickFix.IsCancelled = false;
                quickFix.Fix();
                completed++;

                if (quickFix.IsCancelled)
                {
                    cancelled++;
                    break;
                }
            }

            // refresh if any quickfix has completed without cancelling:
            if (completed != 0 && cancelled < completed)
            {
                Task.Run(() => ExecuteRefreshCommandAsync(null));
            }
        }

        private void ExecuteQuickFixCommand(object parameter)
        {
            var quickFix = parameter as CodeInspectionQuickFix;
            if (quickFix == null)
            {
                return;
            }

            ExecuteQuickFixes(new[] {quickFix});
        }

        private bool CanExecuteQuickFixCommand(object parameter)
        {
            var quickFix = parameter as CodeInspectionQuickFix;
            return !IsBusy && quickFix != null;
        }

        private bool _canExecuteQuickFixInModule;
        public bool CanExecuteQuickFixInModule
        {
            get { return _canExecuteQuickFixInModule; }
            set { _canExecuteQuickFixInModule = value; OnPropertyChanged(); }
        }

        private void ExecuteQuickFixInModuleCommand(object parameter)
        {
            if (_defaultFix == null)
            {
                return;
            }

            var selectedResult = SelectedItem as InspectionResultBase;
            if (selectedResult == null)
            {
                return;
            }

            var items = _results.Where(result => result.Inspection == SelectedInspection
                && result.QualifiedSelection.QualifiedName == selectedResult.QualifiedSelection.QualifiedName)
                .Select(item => item.QuickFixes.Single(fix => fix.GetType() == _defaultFix.GetType()))
                .OrderByDescending(item => item.Selection.Selection.EndLine)
                .ThenByDescending(item => item.Selection.Selection.EndColumn);

            ExecuteQuickFixes(items);
        }

        private bool _canExecuteQuickFixInProject;
        public bool CanExecuteQuickFixInProject
        {
            get { return _canExecuteQuickFixInProject; }
            set { _canExecuteQuickFixInProject = value; OnPropertyChanged(); }
        }

        private void ExecuteDisableInspectionCommand(object parameter)
        {
            if (_selectedInspection == null)
            {
                return;
            }

            var config = _configService.LoadConfiguration();

            var setting = config.UserSettings.CodeInspectionSettings.CodeInspections.Single(e => e.Name == _selectedInspection.Name);
            setting.Severity = CodeInspectionSeverity.DoNotShow;

            Task.Run(() => _configService.SaveConfiguration(config)).ContinueWith(t => ExecuteRefreshCommandAsync(null));
        }

        private bool _canDisableInspection;

        public bool CanDisableInspection
        {
            get { return _canDisableInspection; }
            set { _canDisableInspection = value; OnPropertyChanged(); }
        }

        private void ExecuteQuickFixInProjectCommand(object parameter)
        {
            if (_defaultFix == null)
            {
                return;
            }

            var items = _results.Where(result => result.Inspection == SelectedInspection)
                .Select(item => item.QuickFixes.Single(fix => fix.GetType() == _defaultFix.GetType()))
                .OrderBy(item => item.Selection.QualifiedName.ComponentName)
                .ThenByDescending(item => item.Selection.Selection.EndLine)
                .ThenByDescending(item => item.Selection.Selection.EndColumn);

            ExecuteQuickFixes(items);
        }

        private void ExecuteCopyResultsCommand(object parameter)
        {
            const string XML_SPREADSHEET_DATA_FORMAT = "XML Spreadsheet";
            if (_results == null)
            {
                return;
            }
            ColumnInfo[] ColumnInfos = { new ColumnInfo("Type"), new ColumnInfo("Project"), new ColumnInfo("Component"), new ColumnInfo("Issue"), new ColumnInfo("Line", hAlignment.Right), new ColumnInfo("Column", hAlignment.Right) };

            var aResults = _results.Select(result => result.ToArray()).ToArray();

            var resource = _results.Count == 1
                ? RubberduckUI.CodeInspections_NumberOfIssuesFound_Singular
                : RubberduckUI.CodeInspections_NumberOfIssuesFound_Plural;

            var title = string.Format(resource, DateTime.Now.ToString(CultureInfo.InvariantCulture), _results.Count);

            var textResults = title + Environment.NewLine + string.Join("", _results.Select(result => result.ToString() + Environment.NewLine).ToArray());
            var csvResults = ExportFormatter.Csv(aResults, title,ColumnInfos);
            var htmlResults = ExportFormatter.HtmlClipboardFragment(aResults, title,ColumnInfos);
            var rtfResults = ExportFormatter.RTF(aResults, title);

            MemoryStream strm1 = ExportFormatter.XmlSpreadsheetNew(aResults, title, ColumnInfos);
            //Add the formats from richest formatting to least formatting
            _clipboard.AppendStream(DataFormats.GetDataFormat(XML_SPREADSHEET_DATA_FORMAT).Name, strm1);
            _clipboard.AppendString(DataFormats.Rtf, rtfResults);
            _clipboard.AppendString(DataFormats.Html, htmlResults);
            _clipboard.AppendString(DataFormats.CommaSeparatedValue, csvResults);
            _clipboard.AppendString(DataFormats.UnicodeText, textResults);

            _clipboard.Flush();
        }

        private bool CanExecuteCopyResultsCommand(object parameter)
        {
            return !IsBusy && _results != null && _results.Any();
        }

        public void Dispose()
        {
            if (_state != null)
            {
                _state.StateChanged -= _state_StateChanged;
            }
        }
    }
}
