using PPeX;
using SB3Utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SB3UtilityPPeX
{
    public class SubfileHybrid : ISubfile, IReadFile
    {
        public ISubfile BaseSubfile;

        //public SubfileHybrid(IDataSource source, string name, string archiveName, ArchiveFileType type) : base(source, name, archiveName, type)
        public SubfileHybrid(ISubfile baseSubfile)
        {
            BaseSubfile = baseSubfile;
        }

        public IDataSource Source => BaseSubfile.Source;

        public string ArchiveName => BaseSubfile.ArchiveName;

        public string Name => BaseSubfile.Name;

        public string EmulatedArchiveName => BaseSubfile.EmulatedArchiveName;

        public string EmulatedName => BaseSubfile.EmulatedName;

        public ulong Size => BaseSubfile.Size;

        public ArchiveFileType Type => BaseSubfile.Type;

        string IReadFile.Name {
            get => BaseSubfile.EmulatedName;
            set { }
        }

        public Stream CreateReadStream()
        {
            return BaseSubfile.GetRawStream();
        }

        public Stream GetRawStream()
        {
            return BaseSubfile.GetRawStream();
        }
    }
}
