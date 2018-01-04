using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SB3Utility
{
    public static class Common
    {
        [Plugin]
        public static xxParser OpenXX([DefaultVar]ppxEditor editor, string arcname, string name)
        {
            var subfile = editor.GetSubfile(arcname, name);
            if (editor.FindSubfile(arcname, name))
            {
                return new xxParser(editor.ReadSubfile(arcname, name), subfile.Name);
            }
            return null;
        }
    }
}
