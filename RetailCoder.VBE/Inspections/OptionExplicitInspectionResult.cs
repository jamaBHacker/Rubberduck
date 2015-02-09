using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Antlr4.Runtime;
using Microsoft.Vbe.Interop;
using Rubberduck.Extensions;
using Rubberduck.VBA;
using Rubberduck.VBA.Nodes;

namespace Rubberduck.Inspections
{
    [ComVisible(false)]
    public class OptionExplicitInspectionResult : CodeInspectionResultBase
    {
        public OptionExplicitInspectionResult(string inspection, CodeInspectionSeverity type, QualifiedModuleName qualifiedName) 
            : base(inspection, type, new CommentNode("", new QualifiedSelection(qualifiedName, Selection.Empty)))
        {
        }

        public override IDictionary<string, Action<VBE>> GetQuickFixes()
        {
            return
                new Dictionary<string, Action<VBE>>
                {
                    {"Specify Option Explicit", SpecifyOptionExplicit}
                };
        }

        private void SpecifyOptionExplicit(VBE vbe)
        {
            var module = vbe.FindCodeModules(QualifiedName).FirstOrDefault();
            if (module == null)
            {
                return;
            }

            module.InsertLines(1, Tokens.Option + " " + Tokens.Explicit + "\n");
        }
    }
}