extern alias _PPeX;
using PPeX = _PPeX.PPeX;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.ComponentModel;
using System.Configuration;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using System.Linq;
using _PPeX.PPeX;

namespace SB3Utility
{
    [Plugin]
    public class ppxEditor : EditedContent
    {
        public ExtendedArchive Archive => Appender.BaseArchive;
        public ExtendedArchiveAppender Appender { get; protected set; }

        protected IEnumerable<ISubfile> totalFiles => Archive.Files.Union(Appender.FilesToAdd).Where(x => !Appender.FilesToRemove.Contains(x.Source));

        public static Dictionary<string, List<ExternalTool>> ExternalTools;

        protected bool contentChanged = false;

        public ppxEditor(string path) : this(new ExtendedArchive(path))
        {

        }

        public ppxEditor(ExtendedArchive archive)
        {
            if (ExternalTools == null && Gui.Config != null)
            {
                ExternalTools = new Dictionary<string, List<ExternalTool>>();

                foreach (SettingsPropertyValue value in Gui.Config.PropertyValues)
                {
                    if (value.PropertyValue.GetType() == typeof(ExternalTool))
                    {
                        ExternalTool tool = (ExternalTool)value.PropertyValue;
                        tool = RegisterExternalTool(tool, false);
                        string toolVar = Gui.Scripting.GetNextVariable("externalTool");
                        Gui.Scripting.Variables.Add(toolVar, tool);
                    }
                }
            }

            Appender = new ExtendedArchiveAppender(archive);
        }

        public bool Changed
        {
            get { return contentChanged; }
            set { contentChanged = value; }
        }

        [Plugin]
        public void SavePPx()
        {
            Appender.Write();
        }

        [Plugin]
        public BackgroundWorker SavePPx(string path, bool keepBackup, string backupExtension, bool background)
        {
            throw new NotImplementedException();

            //return Parser.WriteArchive(path, keepBackup, backupExtension, background);
        }

        [Plugin]
        public void ReplaceSubfile(IWriteFile file)
        {
            throw new NotImplementedException();
            
            /*
            int index = FindSubfile(file.Name);
            if (index < 0)
            {
                throw new Exception("Couldn't find the subfile " + file.Name);
            }

            Parser.Subfiles.RemoveAt(index);
            Parser.Subfiles.Insert(index, file);
            */
        }
        
        protected void AddSubfile(string arcname, string path)
        {
            string name = Path.GetFileName(path);

            Appender.FilesToAdd.Add(new Subfile(
                new FileSource(path),
                name,
                arcname));

            Changed = true;
        }

        [Plugin]
        public void AddSubfile(string arcname, string path, bool replace)
        {
            string name = Path.GetFileName(path);

            bool found = FindSubfile(arcname, name);
            if (!replace && found)
                return;
            else if (replace && found)
                RemoveSubfile(arcname, name);
            
            AddSubfile(arcname, path);
            Changed = true;
        }

        [Plugin]
        public void AddSubfiles(string arcname, string path, bool replace)
        {
            foreach (string file in Directory.EnumerateFiles(Path.GetDirectoryName(path), Path.GetFileName(path)))
            {
                AddSubfile(arcname, file, replace);
            }
        }

        [Plugin]
        public void RemoveSubfile(string arcname, string name)
        {
            if (!FindSubfile(arcname, name))
            {
                throw new Exception("Couldn't find the subfile " + name);
            }

            var file = GetSubfile(arcname, name);

            if (file.Source is ArchiveFileSource)
                Appender.FilesToRemove.Add(file.Source as ArchiveFileSource);
            else
                Appender.FilesToAdd.Remove(file);

            Changed = true;
        }

        [Plugin]
        public string RenameSubfile(string subfile, string newName)
        {
            throw new NotImplementedException();

            /*
            int index = FindSubfile(subfile);
            if (index < 0)
            {
                throw new Exception("Couldn't find the subfile " + subfile);
            }

            newName = newName.Trim();
            if (!Utility.ValidFilePath(newName))
            {
                throw new Exception("The name is invalid");
            }

            if (FindSubfile(newName) >= 0)
            {
                throw new Exception("A subfile with " + newName + " already exists");
            }

            Parser.Subfiles[index].Name = newName;
            Changed = true;
            return newName;
            */
        }

        public bool FindSubfile(string archiveName, string name)
        {
            return totalFiles.Any(x => x.ArchiveName == archiveName && x.Name == name);
        }

        public ISubfile GetSubfile(string archiveName, string name)
        {
#warning should names be case sensitive?
            return totalFiles.FirstOrDefault(x => x.ArchiveName == archiveName && x.Name == name);
        }

        public IList<ISubfile> GetSubfiles(string archiveName)
        {
            var files = totalFiles.Where(x => x.ArchiveName == archiveName).ToList();

            for (int i = 0; i < files.Count; i++)
            {
                if (files[i].Type != ArchiveFileType.XggAudio)
                    using (var decoder = PPeX.Encoders.EncoderFactory.GetDecoder(Stream.Null, Archive, files[i].Type))
                    {
                        files[i] = new ArchiveSubfile(files[i].Source as ArchiveFileSource, decoder.NameTransform(files[i].Name));
                    }
            }

            return files;
        }

        [Plugin]
        public Stream ReadSubfile(string archiveName, string name)
        {
            var file = GetSubfile(archiveName, name);

            if (file == null)
            {
                throw new Exception("Couldn't find the subfile " + archiveName + "/" + name);
            }

            return file.GetRawStream();
        }

        [Plugin]
        public void ExportSubfile(string arcname, string name, string path)
        {
            var subfile = GetSubfile(arcname, name);

            using (FileStream fs = new FileStream(path, FileMode.Create))
                if (subfile.Type != ArchiveFileType.XggAudio)
                {
                    using (Stream stream = subfile.GetRawStream())
                        stream.CopyTo(fs);
                }
                else
                {
                    using (Stream stream = (subfile.Source as ArchiveFileSource).GetRawStream())
                        stream.CopyTo(fs);
                }
        }

        [Plugin]
        public void ExportPPx([DefaultVar]string path)
        {
            if (path == String.Empty)
            {
                path = @".\";
            }
            DirectoryInfo dir = new DirectoryInfo(path);
            if (!dir.Exists)
            {
                dir.Create();
            }

            foreach (var file in Archive.Files)
            {
                DirectoryInfo arcDir = new DirectoryInfo(dir.FullName + "\\" + file.ArchiveName);
                if (!arcDir.Exists)
                    arcDir.Create();

                ExportSubfile(file.ArchiveName, file.Name, arcDir.FullName + "\\" + file.Name);
            }
        }

        [Plugin]
        public static ExternalTool RegisterExternalTool(string extension, int ppFormatIndex, string toolPath, string toText, string toBin)
        {
            return RegisterExternalTool(extension, ppFormatIndex, toolPath, toText, toBin, true);
        }

        public static ExternalTool RegisterExternalTool(string extension, int ppFormatIndex, string toolPath, string toText, string toBin, bool save)
        {
            ExternalTool tool = new ExternalTool(extension, ppFormatIndex, toolPath, toText, toBin);
            return RegisterExternalTool(tool, save);
        }

        [Plugin]
        public static ExternalTool RegisterExternalTool(ExternalTool tool, bool save)
        {
            try
            {
                string extensionKey = tool.Extension.ToUpper();
                List<ExternalTool> extList;
                if (!ExternalTools.TryGetValue(extensionKey, out extList))
                {
                    extList = new List<ExternalTool>();
                    ExternalTools.Add(extensionKey, extList);
                }
                else
                {
                    foreach (ExternalTool presentTool in extList)
                    {
                        if (presentTool.ToolPath.Equals(tool.ToolPath, StringComparison.InvariantCultureIgnoreCase))
                        {
                            presentTool.ToTextOptions = tool.ToTextOptions;
                            presentTool.ToBinaryOptions = tool.ToBinaryOptions;
                            presentTool.ppFormatIndex = tool.ppFormatIndex;
                            tool = presentTool;
                            return tool;
                        }
                    }
                }
                extList.Add(tool);
                return tool;
            }
            finally
            {
                if (save)
                {
                    bool stored = false;
                    HashSet<int> usedIndices = new HashSet<int>();
                    foreach (SettingsPropertyValue value in Gui.Config.PropertyValues)
                    {
                        if (value.Name.StartsWith("ExternalTool"))
                        {
                            ExternalTool newTool = (ExternalTool)value.PropertyValue;
                            if (newTool.ToolPath.Equals(tool.ToolPath, StringComparison.InvariantCultureIgnoreCase))
                            {
                                Gui.Config[value.Name] = tool;
                                stored = true;
                                break;
                            }
                            string intStr = value.Name.Substring("ExternalTool".Length);
                            usedIndices.Add(Int32.Parse(intStr));
                        }
                    }
                    if (!stored)
                    {
                        int smallestFreeTool;
                        for (smallestFreeTool = 0; smallestFreeTool < usedIndices.Count; smallestFreeTool++)
                        {
                            if (!usedIndices.Contains(smallestFreeTool))
                            {
                                break;
                            }
                        }
                        if (smallestFreeTool >= 30/*Settings.MaxExternalTools*/)
                        {
                            Report.ReportLog("Cant persist more than 30 external tools.");
                        }
                        else
                        {
                            SettingsProperty newProp = new SettingsProperty(
                                "ExternalTool" + smallestFreeTool, typeof(ExternalTool),
                                Gui.Config.Providers[Path.GetFileName(System.Reflection.Assembly.GetEntryAssembly().Location)],
                                false,
                                tool,
                                SettingsSerializeAs.String,
                                null,
                                true, true
                            );
                            Gui.Config.Properties.Add(newProp);
                            SettingsPropertyValue newValue = new SettingsPropertyValue(newProp);
                            newValue.PropertyValue = tool;
                            Gui.Config.PropertyValues.Add(newValue);
                        }
                    }
                }
            }
        }

        [Plugin]
        public static void UnregisterExternalTool(ExternalTool tool)
        {
            List<ExternalTool> extList;
            if (ExternalTools.TryGetValue(tool.Extension, out extList))
            {
                for (int i = 0; i < extList.Count; i++)
                {
                    if (tool == extList[i])
                    {
                        extList.RemoveAt(i);

                        foreach (SettingsPropertyValue value in Gui.Config.PropertyValues)
                        {
                            if (value.PropertyValue.GetType() == typeof(ExternalTool))
                            {
                                ExternalTool settingsTool = (ExternalTool)value.PropertyValue;
                                if (settingsTool.ToolPath == tool.ToolPath)
                                {
                                    Gui.Config.PropertyValues.Remove(value.Name);
                                    Gui.Config.Properties.Remove(value.Name);
                                    break;
                                }
                            }
                        }
                        return;
                    }
                }
            }
        }
    }
}
