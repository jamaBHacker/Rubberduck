﻿using Antlr4.Runtime;
using Rubberduck.VBEditor;
using System;
using System.Collections.Generic;
using System.Linq;
using Rubberduck.Parsing.Symbols;

namespace Rubberduck.Inspections
{
    public class FunctionReturnValueNotUsedInspectionResult : InspectionResultBase
    {
        private readonly IEnumerable<CodeInspectionQuickFix> _quickFixes;
        private QualifiedMemberName _memberName;

        public FunctionReturnValueNotUsedInspectionResult(
            IInspection inspection,
            ParserRuleContext context,
            QualifiedMemberName qualifiedName,
            IEnumerable<string> returnStatements,
            Declaration target)
            : this(inspection, context, qualifiedName, returnStatements, new List<Tuple<ParserRuleContext, QualifiedSelection, IEnumerable<string>>>(), target)
        {
        }

        public FunctionReturnValueNotUsedInspectionResult(
            IInspection inspection,
            ParserRuleContext context,
            QualifiedMemberName qualifiedName,
            IEnumerable<string> returnStatements,
            IEnumerable<Tuple<ParserRuleContext, QualifiedSelection, IEnumerable<string>>> children,
            Declaration target)
            : base(inspection, qualifiedName.QualifiedModuleName, context, target)
        {
            var root = new ConvertToProcedureQuickFix(context, QualifiedSelection, returnStatements);
            var compositeFix = new CompositeCodeInspectionFix(root);
            children.ToList().ForEach(child => compositeFix.AddChild(new ConvertToProcedureQuickFix(child.Item1, child.Item2, child.Item3)));
            _quickFixes = new[]
            {
                compositeFix
            };
        }

        public override IEnumerable<CodeInspectionQuickFix> QuickFixes { get { return _quickFixes; } }

        public override string Description
        {
            get
            {
                return string.Format(InspectionsUI.FunctionReturnValueNotUsedInspectionResultFormat, Target.IdentifierName);
            }
        }
    }
}
