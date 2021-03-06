﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Vbe.Interop;
using Rubberduck.Parsing.Symbols;
using Rubberduck.UI;
using Rubberduck.UI.Controls;
using Rubberduck.VBEditor;
using Rubberduck.VBEditor.Extensions;
using Rubberduck.VBEditor.VBEHost;

namespace Rubberduck.UnitTesting
{
    public class TestMethod : ViewModelBase, IEquatable<TestMethod>, INavigateSource
    {
        private readonly ICollection<AssertCompletedEventArgs> _assertResults = new List<AssertCompletedEventArgs>();
        private readonly IHostApplication _hostApp;

        public TestMethod(Declaration declaration, VBE vbe)
        {
            _declaration = declaration;
            _qualifiedMemberName = declaration.QualifiedName;
            _hostApp = vbe.HostApplication();
        }

        private Declaration _declaration;
        public Declaration Declaration { get { return _declaration; } }

        public void SetDeclaration(Declaration declaration)
        {
            _declaration = declaration;
        }

        private readonly QualifiedMemberName _qualifiedMemberName;
        public QualifiedMemberName QualifiedMemberName { get { return _qualifiedMemberName; } }

        public void Run()
        {
            _assertResults.Clear(); //clear previous results to account for changes being made

            AssertCompletedEventArgs result;
            var duration = new TimeSpan();
            var startTime = DateTime.Now;
            try
            {
                AssertHandler.OnAssertCompleted += HandleAssertCompleted;
                _hostApp.Run(QualifiedMemberName);
                AssertHandler.OnAssertCompleted -= HandleAssertCompleted;
                
                result = EvaluateResults();
            }
            catch(Exception exception)
            {
                result = new AssertCompletedEventArgs(TestOutcome.Inconclusive, "Test raised an error. " + exception.Message);
            }
            var endTime = DateTime.Now;
            UpdateResult(result.Outcome, result.Message, duration.Milliseconds, startTime, endTime);
        }
        
        public void UpdateResult(TestOutcome outcome, string message = "", long duration = 0, DateTime? startTime = null, DateTime? endTime = null)
        {
            Result.SetValues(outcome, message, duration, startTime, endTime);
            OnPropertyChanged("Result");
        }

        private TestResult _result = new TestResult(TestOutcome.Unknown);
        public TestResult Result
        {
            get { return _result; } 
            set { _result = value; OnPropertyChanged(); }
        }

        void HandleAssertCompleted(object sender, AssertCompletedEventArgs e)
        {
            _assertResults.Add(e);
        }

        private AssertCompletedEventArgs EvaluateResults()
        {
            var result = new AssertCompletedEventArgs(TestOutcome.Succeeded);

            if (_assertResults.Any(assertion => assertion.Outcome == TestOutcome.Failed || assertion.Outcome == TestOutcome.Inconclusive))
            {
                result = _assertResults.First(assertion => assertion.Outcome == TestOutcome.Failed || assertion.Outcome == TestOutcome.Inconclusive);
            }

            return result;
        }

        public NavigateCodeEventArgs GetNavigationArgs()
        {
            try
            {
                var moduleName = QualifiedMemberName.QualifiedModuleName;
                var methodName = QualifiedMemberName.MemberName;
                var module = moduleName.Component.CodeModule;

                var startLine = module.get_ProcStartLine(methodName, vbext_ProcKind.vbext_pk_Proc);
                var endLine = startLine + module.get_ProcCountLines(methodName, vbext_ProcKind.vbext_pk_Proc);
                var endLineColumns = module.get_Lines(endLine, 1).Length;

                var selection = new Selection(startLine, 1, endLine, endLineColumns == 0 ? 1 : endLineColumns);
                return new NavigateCodeEventArgs(new QualifiedSelection(moduleName, selection));
            }
            catch (COMException)
            {
                return null;
            }
        }

        public object[] ToArray()
        {
            return new object[] { QualifiedMemberName.QualifiedModuleName.ProjectTitle, QualifiedMemberName.QualifiedModuleName.ComponentTitle, QualifiedMemberName.MemberName, 
                _result.Outcome.ToString(), _result.Output, _result.StartTime.ToString(CultureInfo.InvariantCulture), _result.EndTime.ToString(CultureInfo.InvariantCulture), _result.Duration };
        }

        public bool Equals(TestMethod other)
        {
            return QualifiedMemberName.Equals(other.QualifiedMemberName);
        }

        public override bool Equals(object obj)
        {
            return obj is TestMethod
                && ((TestMethod)obj).QualifiedMemberName.Equals(QualifiedMemberName);
        }

        public override int GetHashCode()
        {
            return QualifiedMemberName.GetHashCode();
        }

        public override string ToString()
        {
            return string.Format("{0}: {1} ({2}ms) {3}", QualifiedMemberName, Result.Outcome, Result.Duration, Result.Output);
        }
    }
}
