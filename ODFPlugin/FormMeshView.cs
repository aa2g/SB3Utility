﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Windows.Forms;
using System.IO;
using SlimDX;
using SlimDX.Direct3D9;
using WeifenLuo.WinFormsUI.Docking;

using SB3Utility;

namespace ODFPlugin
{
	[Plugin]
	[PluginOpensFile(".odf")]
	public partial class FormMeshView : DockContent
	{
		private enum MeshExportFormat
		{
			[Description("Metasequoia")]
			Mqo,
			[Description("Collada (FBX 2012.2)")]
			ColladaFbx,
			[Description("FBX 2012.2")]
			Fbx,
			[Description("AutoCAD DXF")]
			Dxf,
			[Description("3D Studio 3DS")]
			_3ds,
			[Description("Alias OBJ")]
			Obj
		}

		private class KeyList<T>
		{
			public List<T> List { get; protected set; }
			public int Index { get; protected set; }

			public KeyList(List<T> list, int index)
			{
				List = list;
				Index = index;
			}
		}

		public string FormVariable { get; protected set; }
		public odfEditor Editor { get; protected set; }
		public string EditorVar { get; protected set; }
		public string ParserVar { get; protected set; }

		string exportDir;
		EditTextBox[][] matMatrixText = new EditTextBox[5][];
		ComboBox[] matTexNameCombo;
		bool SetComboboxEvent = false;

		int loadedFrame;
		Tuple<int, int> loadedBone;
		odfBone highlightedBone;
		int loadedMesh;
		int loadedMaterial;
		int loadedTexture;

		Dictionary<int, List<KeyList<odfMaterial>>> crossRefMeshMaterials = new Dictionary<int, List<KeyList<odfMaterial>>>();
		Dictionary<int, List<KeyList<odfTexture>>> crossRefMeshTextures = new Dictionary<int, List<KeyList<odfTexture>>>();
		Dictionary<int, List<KeyList<odfMesh>>> crossRefMaterialMeshes = new Dictionary<int, List<KeyList<odfMesh>>>();
		Dictionary<int, List<KeyList<odfTexture>>> crossRefMaterialTextures = new Dictionary<int, List<KeyList<odfTexture>>>();
		Dictionary<int, List<KeyList<odfMesh>>> crossRefTextureMeshes = new Dictionary<int, List<KeyList<odfMesh>>>();
		Dictionary<int, List<KeyList<odfMaterial>>> crossRefTextureMaterials = new Dictionary<int, List<KeyList<odfMaterial>>>();
		Dictionary<int, int> crossRefMeshMaterialsCount = new Dictionary<int, int>();
		Dictionary<int, int> crossRefMeshTexturesCount = new Dictionary<int, int>();
		Dictionary<int, int> crossRefMaterialMeshesCount = new Dictionary<int, int>();
		Dictionary<int, int> crossRefMaterialTexturesCount = new Dictionary<int, int>();
		Dictionary<int, int> crossRefTextureMeshesCount = new Dictionary<int, int>();
		Dictionary<int, int> crossRefTextureMaterialsCount = new Dictionary<int, int>();

		List<RenderObjectODF> renderObjectMeshes;
		List<int> renderObjectIds;

		private TreeNode[] prevMorphProfileNodes = null;

		private bool listViewItemSyncSelectedSent = false;
		private bool propertiesChanged = false;

		public FormMeshView(string path, string variable)
		{
			InitializeComponent();
			Properties.Settings.Default.PropertyChanged += PropertyChangedEventHandler;

			this.ShowHint = DockState.Document;
			this.Text = Path.GetFileName(path);
			this.ToolTipText = path;
			this.exportDir = Path.GetDirectoryName(path) + @"\" + Path.GetFileNameWithoutExtension(path);

			ParserVar = Gui.Scripting.GetNextVariable("odfParser");
			string parserCommand = ParserVar + " = OpenODF(path=\"" + path + "\")";
			odfParser parser = (odfParser)Gui.Scripting.RunScript(parserCommand);

			EditorVar = Gui.Scripting.GetNextVariable("odfEditor");
			string editorCommand = EditorVar + " = odfEditor(parser=" + ParserVar + ")";
			Editor = (odfEditor)Gui.Scripting.RunScript(editorCommand);

			Init();
			LoadODF();
		}

		private void PropertyChangedEventHandler(object sender, PropertyChangedEventArgs e)
		{
			propertiesChanged = true;
		}

		void CustomDispose()
		{
			try
			{
				if (propertiesChanged)
				{
					Properties.Settings.Default.Save();
					propertiesChanged = false;
				}
				DisposeRenderObjects();
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		void DisposeRenderObjects()
		{
			foreach (ListViewItem item in listViewMesh.SelectedItems)
			{
				Gui.Renderer.RemoveRenderObject(renderObjectIds[(int)item.Tag]);
			}

			if (renderObjectMeshes != null)
			{
				for (int i = 0; i < renderObjectMeshes.Count; i++)
				{
					if (renderObjectMeshes[i] != null)
					{
						renderObjectMeshes[i].Dispose();
						renderObjectMeshes[i] = null;
					}
				}
			}
		}

		void Init()
		{
			panelTexturePic.Resize += new EventHandler(panelTexturePic_Resize);

			matTexNameCombo = new ComboBox[4] { comboBoxMatTex1, comboBoxMatTex2, comboBoxMatTex3, comboBoxMatTex4 };

			matMatrixText[0] = new EditTextBox[4] { textBoxMatDiffuseR, textBoxMatDiffuseG, textBoxMatDiffuseB, textBoxMatDiffuseA };
			matMatrixText[1] = new EditTextBox[4] { textBoxMatAmbientR, textBoxMatAmbientG, textBoxMatAmbientB, textBoxMatAmbientA };
			matMatrixText[2] = new EditTextBox[4] { textBoxMatSpecularR, textBoxMatSpecularG, textBoxMatSpecularB, textBoxMatSpecularA };
			matMatrixText[3] = new EditTextBox[4] { textBoxMatEmissiveR, textBoxMatEmissiveG, textBoxMatEmissiveB, textBoxMatEmissiveA };
			matMatrixText[4] = new EditTextBox[2] { textBoxMatSpecularPower, textBoxMatUnknown1 };

			InitDataGridViewSRT(dataGridViewFrameSRT);
			InitDataGridViewMatrix(dataGridViewFrameMatrix);
			InitDataGridViewSRT(dataGridViewBoneSRT);
			InitDataGridViewMatrix(dataGridViewBoneMatrix);

			textBoxFrameName.AfterEditTextChanged += new EventHandler(textBoxFrameName_AfterEditTextChanged);
			textBoxFrameID.AfterEditTextChanged += new EventHandler(textBoxFrameID_AfterEditTextChanged);
			textBoxBoneFrameID.AfterEditTextChanged += new EventHandler(textBoxBoneFrameID_AfterEditTextChanged);
			textBoxMeshName.AfterEditTextChanged += new EventHandler(textBoxMeshName_AfterEditTextChanged);
			textBoxMeshID.AfterEditTextChanged += new EventHandler(textBoxMeshID_AfterEditTextChanged);
			textBoxMeshInfo.AfterEditTextChanged += new EventHandler(textBoxMeshInfo_AfterEditTextChanged);
			textBoxMeshObjName.AfterEditTextChanged += new EventHandler(textBoxMeshObjName_AfterEditTextChanged);
			textBoxMeshObjID.AfterEditTextChanged += new EventHandler(textBoxMeshObjID_AfterEditTextChanged);
			textBoxMeshObjInfo.AfterEditTextChanged += new EventHandler(textBoxMeshObjInfo_AfterEditTextChanged);
			textBoxMatName.AfterEditTextChanged += new EventHandler(textBoxMatName_AfterEditTextChanged);
			textBoxMatID.AfterEditTextChanged += new EventHandler(textBoxMatID_AfterEditTextChanged);
			textBoxTexName.AfterEditTextChanged += new EventHandler(textBoxTexName_AfterEditTextChanged);
			textBoxTexID.AfterEditTextChanged += new EventHandler(textBoxTexID_AfterEditTextChanged);

			ColumnSubmeshMaterial.DisplayMember = "Item1";
			ColumnSubmeshMaterial.ValueMember = "Item2";
			ColumnSubmeshMaterial.DefaultCellStyle.NullValue = "(invalid)";

			for (int i = 0; i < matMatrixText.Length; i++)
			{
				for (int j = 0; j < matMatrixText[i].Length; j++)
				{
					matMatrixText[i][j].AfterEditTextChanged += new EventHandler(matMatrixText_AfterEditTextChanged);
				}
			}

			for (int i = 0; i < matTexNameCombo.Length; i++)
			{
				matTexNameCombo[i].Tag = i;
				matTexNameCombo[i].SelectedIndexChanged += new EventHandler(matTexNameCombo_SelectedIndexChanged);
			}

			this.groupBoxExportOptions.Tag = false;
			this.groupBoxMeshObjects.Tag = true;
			this.groupBoxMeshTextures.Tag = true;
			this.groupBoxMaterialExtraSetsUnknowns.Tag = true;
			this.groupBoxMaterialProperties.Tag = true;
			this.groupBoxFrameUnknowns.Tag = true;
			this.groupBoxTXPT.Tag = true;

			if (this.closeViewFilesAtStartToolStripMenuItem.Checked)
			{
				DockPanel panel = Gui.Docking.DockFiles.PanelPane.DockPanel;
				Gui.Docking.DockFiles.Hide();
				Gui.Docking.DockEditors.Show(panel, DockState.Document);
			}
			Gui.Docking.ShowDockContent(this, Gui.Docking.DockEditors);

			List<DockContent> formMeshViewList;
			if (Gui.Docking.DockContents.TryGetValue(typeof(FormMeshView), out formMeshViewList))
			{
				var listCopy = new List<FormMeshView>(formMeshViewList.Count);
				for (int i = 0; i < formMeshViewList.Count; i++)
				{
					listCopy.Add((FormMeshView)formMeshViewList[i]);
				}

				string path = ((odfParser)Gui.Scripting.Variables[ParserVar]).ODFPath;
				foreach (var form in listCopy)
				{
					if (form != this)
					{
						var formParser = (odfParser)Gui.Scripting.Variables[form.ParserVar];
						if (formParser.ODFPath == path)
						{
							form.Close();
						}
					}
				}
			}

			MeshExportFormat[] values = Enum.GetValues(typeof(MeshExportFormat)) as MeshExportFormat[];
			string[] descriptions = new string[values.Length];
			for (int i = 0; i < descriptions.Length; i++)
			{
				descriptions[i] = values[i].GetDescription();
			}
			comboBoxMeshExportFormat.Items.AddRange(descriptions);
			comboBoxMeshExportFormat.SelectedIndex = 2;
		}

		private void ClearControl(Control control)
		{
			if (control is TextBox)
			{
				TextBox textBox = (TextBox)control;
//				textBox.TextChanged -= new EventHandler(control_TextChanged);
				textBox.Text = String.Empty;
			}
			else if (control is ComboBox)
			{
				ComboBox comboBox = (ComboBox)control;
//				comboBox.SelectedIndexChanged -= new EventHandler(comboBox_SelectedIndexChanged);
				comboBox.SelectedIndex = -1;
			}
			else if (control is CheckBox)
			{
				CheckBox checkBox = (CheckBox)control;
				checkBox.Checked = false;
			}
			else if (control is ListView)
			{
				ListView listView = (ListView)control;
				listView.Items.Clear();
			}
			else if (control is GroupBox)
			{
				if (control.Tag != null && (bool)control.Tag)
				{
					GroupBox group = (GroupBox)control;
					foreach (Control control2 in group.Controls)
						ClearControl(control2);
				}
			}
			else if (control is TabPage)
			{
				TabPage tab = (TabPage)control;
				foreach (Control control2 in tab.Controls)
					ClearControl(control2);
			}
		}

		void InitDataGridViewSRT(DataGridViewEditor view)
		{
			DataTable tableSRT = new DataTable();
			tableSRT.Columns.Add(" ", typeof(string));
			tableSRT.Columns[0].ReadOnly = true;
			tableSRT.Columns.Add("X", typeof(float));
			tableSRT.Columns.Add("Y", typeof(float));
			tableSRT.Columns.Add("Z", typeof(float));
			tableSRT.Rows.Add(new object[] { "Translate", 0f, 0f, 0f });
			tableSRT.Rows.Add(new object[] { "Rotate", 0f, 0f, 0f });
			tableSRT.Rows.Add(new object[] { "Scale", 1f, 1f, 1f });
			view.Initialize(tableSRT, new DataGridViewEditor.ValidateCellDelegate(ValidateCellSRT), 3);
			view.Scroll += new ScrollEventHandler(dataGridViewEditor_Scroll);

			view.Columns[0].DefaultCellStyle = view.ColumnHeadersDefaultCellStyle;
			for (int i = 0; i < view.Columns.Count; i++)
			{
				view.Columns[i].SortMode = DataGridViewColumnSortMode.NotSortable;
			}
		}

		void InitDataGridViewMatrix(DataGridViewEditor view)
		{
			DataTable tableMatrix = new DataTable();
			tableMatrix.Columns.Add("1", typeof(float));
			tableMatrix.Columns.Add("2", typeof(float));
			tableMatrix.Columns.Add("3", typeof(float));
			tableMatrix.Columns.Add("4", typeof(float));
			tableMatrix.Rows.Add(new object[] { 1f, 0f, 0f, 0f });
			tableMatrix.Rows.Add(new object[] { 0f, 1f, 0f, 0f });
			tableMatrix.Rows.Add(new object[] { 0f, 0f, 1f, 0f });
			tableMatrix.Rows.Add(new object[] { 0f, 0f, 0f, 1f });
			view.Initialize(tableMatrix, new DataGridViewEditor.ValidateCellDelegate(ValidateCellSingle), 4);
			view.Scroll += new ScrollEventHandler(dataGridViewEditor_Scroll);

			for (int i = 0; i < view.Columns.Count; i++)
			{
				view.Columns[i].SortMode = DataGridViewColumnSortMode.NotSortable;
			}
		}

		void dataGridViewEditor_Scroll(object sender, ScrollEventArgs e)
		{
			try
			{
				e.NewValue = e.OldValue;
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		bool ValidateCellSRT(string s, int row, int col)
		{
			if (col == 0)
			{
				return true;
			}
			else
			{
				return ValidateCellSingle(s, row, col);
			}
		}

		bool ValidateCellSingle(string s, int row, int col)
		{
			float f;
			if (Single.TryParse(s, out f))
			{
				return true;
			}
			return false;
		}

		void RecreateRenderObjects()
		{
			DisposeRenderObjects();

			renderObjectMeshes = new List<RenderObjectODF>(new RenderObjectODF[Editor.Parser.MeshSection.Count]);
			renderObjectIds = new List<int>(new int[Editor.Parser.MeshSection.Count]);

			foreach (ListViewItem item in listViewMesh.SelectedItems)
			{
				int idx = (int)item.Tag;
				odfMesh mesh = Editor.Parser.MeshSection[idx];
				HashSet<int> meshIDs = new HashSet<int>() { (int)mesh.Id };
				renderObjectMeshes[idx] = new RenderObjectODF(Editor.Parser, meshIDs);

				RenderObjectODF renderObj = renderObjectMeshes[idx];
				renderObjectIds[idx] = Gui.Renderer.AddRenderObject(renderObj);
			}

			HighlightSubmeshes();
			if (highlightedBone != null)
				HighlightBone(highlightedBone, true);
		}

		void RenameListViewItems<T>(List<T> list, ListView listView, T obj, string name)
		{
			foreach (ListViewItem item in listView.Items)
			{
				if (list[(int)item.Tag].Equals(obj))
				{
					item.Text = name;
					break;
				}
			}
			listView.Columns[0].AutoResize(ColumnHeaderAutoResizeStyle.ColumnContent);
		}

		private void LoadODF()
		{
			if (Editor.Parser.MeshSection != null && (renderObjectMeshes == null || renderObjectMeshes.Count != Editor.Parser.MeshSection.Count))
			{
				renderObjectMeshes = new List<RenderObjectODF>(new RenderObjectODF[Editor.Parser.MeshSection.Count]);
				renderObjectIds = new List<int>(new int[Editor.Parser.MeshSection.Count]);
			}

			InitFormat();

			InitFrames();
			if (Editor.Parser.MeshSection != null)
				InitMeshes();
			if (Editor.Parser.MaterialSection != null)
				InitMaterials();
			if (Editor.Parser.TextureSection != null)
				InitTextures();

			if (Editor.Parser.MeshSection != null && Editor.Parser.MaterialSection != null && Editor.Parser.TextureSection != null)
				RecreateCrossRefs();

			InitMorphs();
			InitAnims();
		}

		void InitFormat()
		{
			int formatType = Editor.Parser.TextureSection != null ? Editor.Parser.TextureSection._FormatType : 
				Editor.Parser.MeshSection != null ? Editor.Parser.MeshSection._FormatType : 0;
			textBoxFormat.Text = formatType.ToString();
		}

		void InitFrames()
		{
			TreeNode objRootNode = CreateFrameTree(Editor.Parser.FrameSection.RootFrame, null);

			if (treeViewObjectTree.Nodes.Count > 0)
			{
				treeViewObjectTree.Nodes.RemoveAt(0);
			}
			treeViewObjectTree.Nodes.Insert(0, objRootNode);
		}

		public TreeNode CreateFrameTree(odfFrame frame, TreeNode parentNode)
		{
			TreeNode newNode = null;
			try
			{
				newNode = new TreeNode(frame.ToString());
				newNode.Tag = new DragSource(EditorVar, typeof(odfFrame), Editor.Frames.IndexOf(frame));

				if ((int)frame.MeshId != 0)
				{
					if (Editor.Parser.MeshSection != null)
					{
						odfMesh mesh = odf.FindMeshListSome(frame.MeshId, Editor.Parser.MeshSection);
						TreeNode meshNode = new TreeNode(mesh.ToString());
						meshNode.Tag = new DragSource(EditorVar, typeof(odfMesh), Editor.Parser.MeshSection.ChildList.IndexOf(mesh));
						newNode.Nodes.Add(meshNode);

						for (int meshObjIdx = 0; meshObjIdx < mesh.Count; meshObjIdx++)
						{
							odfSubmesh meshObj = mesh[meshObjIdx];
							TreeNode meshObjNode = new TreeNode(meshObj.ToString() + ", vertices: " + meshObj.NumVertices + ", faces: " + meshObj.NumVertexIndices / 3);
							meshObjNode.Tag = meshObj;

							String missingBoneFrameWarning = null;
							if (Editor.Parser.EnvelopeSection != null)
							{
								int numBoneLists = 0;
								for (int envIdx = 0; envIdx < Editor.Parser.EnvelopeSection.Count; envIdx++)
								{
									odfBoneList boneList = Editor.Parser.EnvelopeSection[envIdx];
									if (boneList.SubmeshId == meshObj.Id)
									{
										++numBoneLists;
										meshObjNode.Text += ", " + boneList.Count + " bone(s)";
										for (int boneIdx = 0; boneIdx < boneList.Count; boneIdx++)
										{
											TreeNode boneNode = new TreeNode();
											try
											{
												odfBone bone = boneList[boneIdx];
												String name = bone.ToString(Editor.Parser.FrameSection.RootFrame);
												boneNode.Text = name;
												boneNode.Tag = new Tuple<odfBone, Tuple<int, int>>(bone, new Tuple<int, int>(envIdx, boneIdx));
											}
											catch (Exception)
											{
												String name = boneList[boneIdx].FrameId.ToString();
												boneNode.Text = "Frame " + name + " missing : skin broken";
												if (missingBoneFrameWarning == null)
													missingBoneFrameWarning = name;
												else
													missingBoneFrameWarning += ", " + name;
											}
											meshObjNode.Nodes.Add(boneNode);
										}
									}
								}
								if (numBoneLists > 1)
									Report.ReportLog(numBoneLists + " bone lists for mesh object " + meshObj.ToString() + " found.");
							}
							if (missingBoneFrameWarning != null)
								Report.ReportLog("Skin of mesh <" + mesh.Name + "> mesh object <" + meshObj.ToString() + "> is missing frames: " + missingBoneFrameWarning);

							meshNode.Nodes.Add(meshObjNode);
						}
					}
					else
						Report.ReportLog("Frame " + frame.Name + " is a mesh frame for " + frame.MeshId  + ", but there is no MeshSection present.");
				}

				if (parentNode != null)
				{
					parentNode.Nodes.Add(newNode);
				}
				for (int i = 0; i < frame.Count; i++)
				{
					CreateFrameTree(frame[i], newNode);
				}
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
			return newNode;
		}

		void InitMeshes()
		{
			ListViewItem[] meshItems = new ListViewItem[Editor.Parser.MeshSection.Count];
			for (int i = 0; i < Editor.Parser.MeshSection.Count; i++)
			{
				odfMesh meshListSome = Editor.Parser.MeshSection[i];
				meshItems[i] = new ListViewItem(meshListSome.ToString());
				meshItems[i].Tag = i;
			}
			listViewMesh.Items.Clear();
			listViewMesh.Items.AddRange(meshItems);
			meshlistHeader.AutoResize(ColumnHeaderAutoResizeStyle.ColumnContent);
		}

		void InitMaterials()
		{
			List<Tuple<string, int>> columnMaterials = new List<Tuple<string, int>>(Editor.Parser.MaterialSection.Count);
			ListViewItem[] materialItems = new ListViewItem[Editor.Parser.MaterialSection.Count];
			for (int i = 0; i < Editor.Parser.MaterialSection.Count; i++)
			{
				odfMaterial mat = Editor.Parser.MaterialSection[i];
				materialItems[i] = new ListViewItem(mat.Name);
				materialItems[i].Tag = i;

				columnMaterials.Add(new Tuple<string, int>(mat.Name, (int)mat.Id));
			}
			listViewMaterial.Items.Clear();
			listViewMaterial.Items.AddRange(materialItems);
			materiallistHeader.AutoResize(ColumnHeaderAutoResizeStyle.ColumnContent);

			ColumnSubmeshMaterial.DataSource = columnMaterials;

			TreeNode materialsNode = new TreeNode("Materials");
			for (int i = 0; i < Editor.Parser.MaterialSection.Count; i++)
			{
				TreeNode matNode = new TreeNode(Editor.Parser.MaterialSection[i].Name);
				matNode.Tag = new DragSource(EditorVar, typeof(odfMaterial), i);
				materialsNode.Nodes.Add(matNode);
			}

			if (treeViewObjectTree.Nodes.Count > 1)
			{
				treeViewObjectTree.Nodes.RemoveAt(1);
			}
			treeViewObjectTree.Nodes.Insert(1, materialsNode);
		}

		void InitTextures()
		{
			for (int i = 0; i < matTexNameCombo.Length; i++)
			{
				matTexNameCombo[i].Items.Clear();
				matTexNameCombo[i].Items.Add("(none)");
			}

			ListViewItem[] textureItems = new ListViewItem[Editor.Parser.TextureSection.Count];
			for (int i = 0; i < Editor.Parser.TextureSection.Count; i++)
			{
				odfTexture tex = Editor.Parser.TextureSection[i];
				textureItems[i] = new ListViewItem(tex.Name);
				textureItems[i].Tag = i;
				for (int j = 0; j < matTexNameCombo.Length; j++)
				{
					matTexNameCombo[j].Items.Add(tex);
				}
			}
			listViewTexture.Items.Clear();
			listViewTexture.Items.AddRange(textureItems);
			texturelistHeader.AutoResize(ColumnHeaderAutoResizeStyle.ColumnContent);

			TreeNode texturesNode = new TreeNode("Textures");
			for (int i = 0; i < Editor.Parser.TextureSection.Count; i++)
			{
				TreeNode texNode = new TreeNode(Editor.Parser.TextureSection[i].Name);
				texNode.Tag = new DragSource(EditorVar, typeof(odfTexture), i);
				texturesNode.Nodes.Add(texNode);
			}

			if (treeViewObjectTree.Nodes.Count > 2)
			{
				treeViewObjectTree.Nodes.RemoveAt(2);
			}
			treeViewObjectTree.Nodes.Insert(2, texturesNode);
		}

		private void InitMorphs()
		{
			if (this.Editor.Parser.MorphSection != null)
			{
				treeViewMorphObj.Nodes.Clear();
				if (this.Editor.Parser.MorphSection.Count > 0)
				{
					treeViewMorphObj.BeginUpdate();
					for (int i = 0; i < this.Editor.Parser.MorphSection.Count; i++)
					{
						odfMorphObject morphObj = this.Editor.Parser.MorphSection[i];
						odfSubmesh meshObj = odf.FindMeshObject(morphObj.SubmeshId, this.Editor.Parser.MeshSection);
						string meshName;
						string meshObjName;
						if (meshObj != null)
						{
							meshName = ((odfMesh)meshObj.Parent).Name;
							if (meshName.Length == 0)
								meshName = ((odfMesh)meshObj.Parent).Id.ToString();
							meshObjName = meshObj.Name;
							if (meshObjName.Length == 0)
								meshObjName = meshObj.Id.ToString();
						}
						else
						{
							Report.ReportLog("Morph object " + morphObj.Name + " has no valid submesh " + morphObj.SubmeshId);
							meshName = "unknown mesh";
							meshObjName = morphObj.SubmeshId.ToString();
						}
						TreeNode morphObjNode = new TreeNode(morphObj.Name + " [" + meshName + "/" + meshObjName + "]");
						morphObjNode.Checked = true;
						morphObjNode.Tag = morphObj;
						treeViewMorphObj.Nodes.Add(morphObjNode);

						for (int j = 0; j < morphObj.Count; j++)
						{
							odfMorphProfile profile = morphObj[j];
							TreeNode profileNode = new TreeNode(j + ": " + profile.Name.ToString());
							profileNode.Tag = profile;
							morphObjNode.Nodes.Add(profileNode);
						}
					}
					treeViewMorphObj.EndUpdate();
					prevMorphProfileNodes = new TreeNode[this.Editor.Parser.MorphSection.Count];
					tabPageMorph.Text = "Morph [" + this.Editor.Parser.MorphSection.Count + "]";
				}
				else
				{
					prevMorphProfileNodes = null;
					if (tabPageMorph.Parent != null)
						tabPageMorph.Parent.Controls.Remove(tabPageMorph);
				}
			}
			else if (tabPageMorph.Parent != null)
				tabPageMorph.Parent.Controls.Remove(tabPageMorph);
		}

		private void InitAnims()
		{
			if (this.Editor.Parser.AnimSection != null)
			{
				createAnimationClipListView(this.Editor.Parser.BANMList, listViewAnimationClip);
				tabPageAnimation.Text = "Animation [" + listViewAnimationClip.Items.Count + "]";

				createAnimationTrackListView(this.Editor.Parser.AnimSection);
//				animationSetMaxKeyframes(animationNodeList);
			}
			else
			{
//				animationSetMaxKeyframes(null);
				if (tabPageAnimation.Parent != null)
					tabPageAnimation.Parent.Controls.Remove(tabPageAnimation);
			}
		}

		private void createAnimationTrackListView(odfANIMSection trackList)
		{
			if (trackList.Count > 0)
			{
				listViewAnimationTrack.BeginUpdate();
				for (int i = 0; i < trackList.Count; i++)
				{
					odfTrack track = trackList[i];
					string numTracks = String.Empty;
					for (int j = 0; j < this.listViewAnimationClip.SelectedItems.Count; j++)
					{
						odfBANMSection banm = (odfBANMSection)this.listViewAnimationClip.SelectedItems[j].Tag;
						for (int k = 0; k < banm.Count; k++)
						{
							odfTrack bTrack = banm[k];
							if (bTrack.BoneFrameId == track.BoneFrameId)
							{
								numTracks += "," + bTrack.KeyframeList.Count;
							}
						}
					}
					odfFrame frame = odf.FindFrame(track.BoneFrameId, this.Editor.Parser.FrameSection.RootFrame);
					ListViewItem item = new ListViewItem(new string[] { frame != null ? frame.Name : track.BoneFrameId + " (orphaned track)", track.KeyframeList.Count.ToString()+numTracks });
					item.Tag = track;
					listViewAnimationTrack.Items.Add(item);
				}
				listViewAnimationTrack.EndUpdate();
			}
		}

		public static void createAnimationClipListView(List<odfBANMSection> clipList, ListView clipListView)
		{
			clipListView.BeginUpdate();
			for (int i = 0; i < clipList.Count; i++)
			{
				odfBANMSection clip = clipList[i];
				string clipName = clip.Name.ToString();
				if (clipName == string.Empty)
					clipName = "unnamed (" + clip.Id.ToString() + ")";
				ListViewItem item = new ListViewItem(new string[] { i.ToString(), clipName, clip.StartKeyframeIndex.ToString(), clip.EndKeyframeIndex.ToString(), clip.Count.ToString() });
				item.Tag = clip;
				clipListView.Items.Add(item);
			}
			clipListView.EndUpdate();
		}

		void LoadFrame(int frameIdx)
		{
			if (frameIdx < 0)
			{
				ClearControl(tabPageFrameView);
				LoadMatrix(Matrix.Identity, dataGridViewFrameSRT, dataGridViewFrameMatrix);
			}
			else
			{
				odfFrame frame = Editor.Frames[frameIdx];
				textBoxFrameName.Text = frame.Name;
				textBoxFrameID.Text = frame.Id.ToString();
				textBoxFrameUnk1.Text = String.Format("{0:X}", frame.Unknown3);
				textBoxFrameUnk2.Text = String.Format("{0:G}", frame.Unknown4[0]);
				textBoxFrameUnk3.Text = String.Format("{0:G}", frame.Unknown4[1]);
				textBoxFrameUnk4.Text = String.Format("{0:G}", frame.Unknown4[2]);
				textBoxFrameUnk5.Text = String.Format("{0:G}", frame.Unknown4[3]);
				textBoxFrameUnk6.Text = String.Format("{0:G}", frame.Unknown4[4]);
				textBoxFrameUnk7.Text = String.Format("{0:G}", frame.Unknown4[5]);
				textBoxFrameUnk8.Text = String.Format("{0:G}", frame.Unknown4[6]);
				textBoxFrameUnk9.Text = String.Format("{0:G}", frame.Unknown4[7]);
				textBoxFrameUnk10.Text = String.Format("{0:G}", frame.Unknown8);
				textBoxFrameUnk11.Text = String.Format("{0:G}", frame.Unknown10);
				textBoxFrameUnk12.Text = String.Format("{0:G}", frame.Unknown12);
				textBoxFrameUnk2_5.Text = String.Format("{0:X}", frame.Unknown6);
				LoadMatrix(frame.Matrix, dataGridViewFrameSRT, dataGridViewFrameMatrix);

				if (Editor.Parser.TXPTSection != null)
				{
					bool clearTXPTcontrols = true;
					odfTxPtList txptList = null;
					for (int i = 0; i < Editor.Parser.TXPTSection.Count; i++)
					{
						txptList = Editor.Parser.TXPTSection[i];
						if (txptList.MeshFrameId == frame.Id)
						{
							textBoxTXPTunknown1.Text = txptList.Unknown1[0].ToString();
							textBoxTXPTunknown2.Text = txptList.Unknown1[1].ToString();
							textBoxTXPTunknown3.Text = txptList.Unknown1[2].ToString();
							textBoxTXPTunknown4.Text = txptList.Unknown1[3].ToString();
							textBoxTXPTunknown5.Text = txptList.Unknown2.ToString();

							listViewTXPTinfos.Items.Clear();
							for (int j = 0; j < txptList.TxPtList.Count; j++)
							{
								odfTxPt txptInfo = txptList.TxPtList[j];
								ListViewItem item = new ListViewItem(new string[8] {
									txptInfo.Index.ToString(), txptInfo.Value.ToString(),
									BitConverter.ToInt32(txptInfo.AlwaysZero16, 0).ToString(),
									BitConverter.ToInt32(txptInfo.AlwaysZero16, 4).ToString(),
									BitConverter.ToInt32(txptInfo.AlwaysZero16, 8).ToString(),
									BitConverter.ToInt32(txptInfo.AlwaysZero16, 12).ToString(),
									txptInfo.Prev.ToString(), txptInfo.Next.ToString()
								});
								item.Tag = txptInfo;
								listViewTXPTinfos.Items.Add(item);
							}
							clearTXPTcontrols = false;
							break;
						}
					}
					if (clearTXPTcontrols)
						ClearControl(groupBoxTXPT);
				}
			}
			loadedFrame = frameIdx;
		}

		void LoadMatrix(Matrix matrix, DataGridView viewSRT, DataGridView viewMatrix)
		{
			Vector3[] srt = FbxUtility.MatrixToSRT(matrix);
			DataTable tableSRT = (DataTable)viewSRT.DataSource;
			for (int i = 0; i < 3; i++)
			{
				tableSRT.Rows[0][i + 1] = srt[2][i];
				tableSRT.Rows[1][i + 1] = srt[1][i];
				tableSRT.Rows[2][i + 1] = srt[0][i];
			}

			DataTable tableMatrix = (DataTable)viewMatrix.DataSource;
			for (int i = 0; i < 4; i++)
			{
				for (int j = 0; j < 4; j++)
				{
					tableMatrix.Rows[i][j] = matrix[i, j];
				}
			}
		}

		void LoadBone(Tuple<int, int> idxPair)
		{
			if (idxPair == null)
			{
				ClearControl(tabPageBoneView);
				LoadMatrix(Matrix.Identity, dataGridViewBoneSRT, dataGridViewBoneMatrix);
			}
			else
			{
				odfBone bone = Editor.Parser.EnvelopeSection[idxPair.Item1][idxPair.Item2];
				textBoxBoneFrameName.Text = odf.FindFrame(bone.FrameId, Editor.Parser.FrameSection.RootFrame).Name;
				textBoxBoneFrameID.Text = bone.FrameId.ToString();
				LoadMatrix(bone.Matrix, dataGridViewBoneSRT, dataGridViewBoneMatrix);
			}
			loadedBone = idxPair;
		}


		private string MeshObjectUnknownsToString(odfSubmesh meshObj)
		{
			return String.Format("{0:X}-{1:X}-{2:X}-{3:X}-{4:D}-{5:X}-{6:G}", meshObj.Unknown1, meshObj.Unknown2, meshObj.Unknown4, meshObj.Unknown5, meshObj.Unknown6, meshObj.Unknown7, BitConverter.ToSingle(meshObj.Unknown8, 0));
		}

		void LoadMesh(int meshIdx)
		{
			dataGridViewMesh.Rows.Clear();
			if (meshIdx < 0)
			{
				textBoxMeshName.Text = String.Empty;
				textBoxMeshID.Text = String.Empty;
				textBoxMeshInfo.Text = String.Empty;
				textBoxMeshObjName.Text = String.Empty;
				textBoxMeshObjInfo.Text = String.Empty;
				textBoxMeshObjID.Text = String.Empty;
			}
			else
			{
				dataGridViewMesh.SelectionChanged -= dataGridViewMesh_SelectionChanged;
				odfMesh mesh = Editor.Parser.MeshSection[meshIdx];
				for (int i = 0; i < mesh.Count; i++)
				{
					odfSubmesh submesh = mesh[i];
					int rowIdx = dataGridViewMesh.Rows.Add(new object[] { submesh.Name.ToString(), submesh.VertexList.Count, submesh.FaceList.Count, (int)submesh.MaterialId, MeshObjectUnknownsToString(submesh) });
					DataGridViewRow row = dataGridViewMesh.Rows[rowIdx];
					row.Tag = submesh;
				}
				dataGridViewMesh.SelectionChanged += dataGridViewMesh_SelectionChanged;
				dataGridViewMesh.ClearSelection();

				textBoxMeshName.Text = mesh.Name;
				textBoxMeshID.Text = mesh.Id.ToString();
				textBoxMeshInfo.Text = mesh.Name.Info;
			}
			checkBoxMeshObjSkinned.Checked = false;
			loadedMesh = meshIdx;
		}

		void LoadMaterial(int matIdx)
		{
			loadedMaterial = -1;

			if (matIdx < 0)
			{
				ClearControl(tabPageMaterialView);
			}
			else
			{
				odfMaterial mat = Editor.Parser.MaterialSection[matIdx];
				textBoxMatName.Text = mat.Name;
				textBoxMatID.Text = mat.Id.ToString();

				comboBoxMatTexMeshObj.Items.Clear();
				for (int i = 0; i < Editor.Parser.MeshSection.Count; i++)
				{
					for (int j = 0; j < Editor.Parser.MeshSection[i].Count; j++)
					{
						odfSubmesh meshObj = Editor.Parser.MeshSection[i][j];
						if (mat.Id == meshObj.MaterialId)
						{
							comboBoxMatTexMeshObj.Items.Add(meshObj);
						}
					}
				}
				if (comboBoxMatTexMeshObj.Items.Count > 0)
					comboBoxMatTexMeshObj.SelectedIndex = 0;
				else
					setMaterialViewTextures();

				comboBoxMatSetSelector.Tag = matIdx;
				comboBoxMatSetSelector.Items.Clear();
				comboBoxMatSetSelector.Items.Add("MAT");
				comboBoxMatSetSelector.SelectedIndex = 0;
				textBoxMatMataUnknown1.Text = String.Empty;
				textBoxMatMataUnknown1.Enabled = false;
				textBoxMatMataUnknown2.Text = String.Empty;
				textBoxMatMataUnknown2.Enabled = false;
				int numMatAddSets = 0;
				if (Editor.Parser.MataSection != null)
				{
					int numMatLists = Editor.Parser.MataSection.Count;
					for (int i = 0; i < numMatLists; i++)
					{
						odfMaterialList matList = Editor.Parser.MataSection[i];
						if (matList.MaterialId == mat.Id)
						{
							numMatAddSets = matList.Count;
							for (int j = 0; j < numMatAddSets; j++)
							{
								comboBoxMatSetSelector.Items.Add("MATA" + j);
							}
							textBoxMatMataUnknown1.Text = matList.Unknown1.ToString();
							textBoxMatMataUnknown1.Enabled = true;
							textBoxMatMataUnknown2.Text = matList.Unknown2.ToString();
							textBoxMatMataUnknown2.Enabled = true;
							break;
						}
					}
				}
				textBoxMatNumAddSets.Text = numMatAddSets.ToString();
			}
			loadedMaterial = matIdx;
		}

		private void setMaterialViewTextures()
		{
			odfSubmesh meshObj = (odfSubmesh)comboBoxMatTexMeshObj.SelectedItem;
			if (Editor.Parser.TextureSection != null && meshObj != null)
			{
				int abort_matTexNameCombo_SelectedIndexChanged = loadedMaterial;
				loadedMaterial = -1;
				for (int i = 0; i < matTexNameCombo.Length; i++)
				{
					matTexNameCombo[i].SelectedIndex = 0;
				}
				for (int texIdx = 0; texIdx < Editor.Parser.TextureSection.Count; texIdx++)
				{
					for (int i = 0; i < matTexNameCombo.Length; i++)
					{
						if (meshObj.TextureIds[i] == Editor.Parser.TextureSection[texIdx].Id)
							matTexNameCombo[i].SelectedIndex = matTexNameCombo[i].FindStringExact(Editor.Parser.TextureSection[texIdx].Name);
					}
				}
				loadedMaterial = abort_matTexNameCombo_SelectedIndexChanged;
			}
		}

		private void setMaterialViewProperties(int source, int matIdx)
		{
			if (source < 0)
				return;

			Color4 diffuse;
			Color4 ambient;
			Color4 specular;
			Color4 emissive;
			float specularPower;
			float unknown1;
			odfMaterial mat = Editor.Parser.MaterialSection[matIdx];
			if (source == 0)
			{
				diffuse = mat.Diffuse;
				ambient = mat.Ambient;
				specular = mat.Specular;
				emissive = mat.Emissive;
				specularPower = mat.SpecularPower;
				unknown1 = mat.Unknown1;
			}
			else
			{
				int propertySetIdx = source - 1;
				odfMaterialList matList = odf.FindMaterialList(mat.Id, Editor.Parser.MataSection);
				odfMaterialPropertySet propertySet = matList[propertySetIdx];
				diffuse = propertySet.Diffuse;
				ambient = propertySet.Ambient;
				specular = propertySet.Specular;
				emissive = propertySet.Emissive;
				specularPower = propertySet.SpecularPower;
				unknown1 = propertySet.Unknown1;
			}

			Color4[] colors = new Color4[] { diffuse, ambient, specular, emissive };
			for (int i = 0; i < colors.Length; i++)
			{
				matMatrixText[i][0].Text = colors[i].Red.ToFloatString();
				matMatrixText[i][1].Text = colors[i].Green.ToFloatString();
				matMatrixText[i][2].Text = colors[i].Blue.ToFloatString();
				matMatrixText[i][3].Text = colors[i].Alpha.ToFloatString();
			}

			matMatrixText[4][0].Text = specularPower.ToFloatString();

			matMatrixText[4][1].Text = unknown1.ToFloatString();
		}

		void LoadTexture(int texIdx)
		{
			if (texIdx < 0)
			{
				textBoxTexName.Text = String.Empty;
				textBoxTexID.Text = String.Empty;
				textBoxTexSize.Text = String.Empty;
				pictureBoxTexture.Image = null;
			}
			else
			{
				odfTexture tex = Editor.Parser.TextureSection[texIdx];
				textBoxTexName.Text = tex.Name;
				textBoxTexID.Text = tex.Id.ToString();

				odfTextureFile texFile = new odfTextureFile(null, Path.GetDirectoryName(Editor.Parser.ODFPath) + Path.DirectorySeparatorChar + tex.TextureFile);
				int fileSize = 0;
				byte[] data;
				using (BinaryReader reader = texFile.DecryptFile(ref fileSize))
				{
					data = reader.ReadBytes(fileSize);
				}
				Texture renderTexture = Texture.FromMemory(Gui.Renderer.Device, data);
				Bitmap bitmap = new Bitmap(Texture.ToStream(renderTexture, ImageFileFormat.Bmp));
				renderTexture.Dispose();
				pictureBoxTexture.Image = bitmap;
				textBoxTexSize.Text = bitmap.Width + "x" + bitmap.Height;

				ResizeImage();
			}
			loadedTexture = texIdx;
		}

		void panelTexturePic_Resize(object sender, EventArgs e)
		{
			try
			{
				ResizeImage();
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		void ResizeImage()
		{
			if (pictureBoxTexture.Image != null)
			{
				Decimal x = (Decimal)panelTexturePic.Width / pictureBoxTexture.Image.Width;
				Decimal y = (Decimal)panelTexturePic.Height / pictureBoxTexture.Image.Height;
				if (x > y)
				{
					pictureBoxTexture.Width = Decimal.ToInt32(pictureBoxTexture.Image.Width * y);
					pictureBoxTexture.Height = Decimal.ToInt32(pictureBoxTexture.Image.Height * y);
				}
				else
				{
					pictureBoxTexture.Width = Decimal.ToInt32(pictureBoxTexture.Image.Width * x);
					pictureBoxTexture.Height = Decimal.ToInt32(pictureBoxTexture.Image.Height * x);
				}
			}
		}

		private void RecreateFrames()
		{
			CrossRefsClear();
			DisposeRenderObjects();
			LoadFrame(-1);
			LoadMesh(-1);
			InitFrames();
			InitMeshes();
			RecreateRenderObjects();
			RecreateCrossRefs();
		}

		private void RecreateMeshes()
		{
			CrossRefsClear();
			DisposeRenderObjects();
			LoadMesh(-1);
			InitFrames();
			InitMeshes();
			InitMaterials();
			InitTextures();
			RecreateRenderObjects();
			RecreateCrossRefs();
		}

		private void RecreateMaterials()
		{
			CrossRefsClear();
			DisposeRenderObjects();
			LoadMaterial(-1);
			InitMaterials();
			RecreateRenderObjects();
			RecreateCrossRefs();
			LoadMesh(loadedMesh);
		}

		private void RecreateTextures()
		{
			CrossRefsClear();
			DisposeRenderObjects();
			LoadTexture(-1);
			InitTextures();
			RecreateRenderObjects();
			RecreateCrossRefs();
			LoadMaterial(loadedMaterial);
		}

		#region Cross-Refs

		private void RecreateCrossRefs()
		{
			CrossRefsClear();

			crossRefMeshMaterials.Clear();
			crossRefMeshTextures.Clear();
			crossRefMaterialMeshes.Clear();
			crossRefMaterialTextures.Clear();
			crossRefTextureMeshes.Clear();
			crossRefTextureMaterials.Clear();
			crossRefMeshMaterialsCount.Clear();
			crossRefMeshTexturesCount.Clear();
			crossRefMaterialMeshesCount.Clear();
			crossRefMaterialTexturesCount.Clear();
			crossRefTextureMeshesCount.Clear();
			crossRefTextureMaterialsCount.Clear();

			var meshes = Editor.Parser.MeshSection.ChildList;
			var materials = Editor.Parser.MaterialSection.ChildList;
			var textures = Editor.Parser.TextureSection.ChildList;

			for (int i = 0; i < meshes.Count; i++)
			{
				crossRefMeshMaterials.Add(i, new List<KeyList<odfMaterial>>(materials.Count));
				crossRefMeshTextures.Add(i, new List<KeyList<odfTexture>>(textures != null ? textures.Count : 0));
				crossRefMaterialMeshesCount.Add(i, 0);
				crossRefTextureMeshesCount.Add(i, 0);
			}

			for (int i = 0; i < materials.Count; i++)
			{
				crossRefMaterialMeshes.Add(i, new List<KeyList<odfMesh>>(meshes.Count));
				crossRefMaterialTextures.Add(i, new List<KeyList<odfTexture>>(textures != null ? textures.Count : 0));
				crossRefMeshMaterialsCount.Add(i, 0);
				crossRefTextureMaterialsCount.Add(i, 0);
			}

			if (textures != null)
			{
				for (int i = 0; i < textures.Count; i++)
				{
					crossRefTextureMeshes.Add(i, new List<KeyList<odfMesh>>(meshes.Count));
					crossRefTextureMaterials.Add(i, new List<KeyList<odfMaterial>>(materials.Count));
					crossRefMeshTexturesCount.Add(i, 0);
					crossRefMaterialTexturesCount.Add(i, 0);
				}

				for (int matIdx = 0; matIdx < materials.Count; matIdx++)
				{
					odfMaterial mat = materials[matIdx];
					for (int i = 0; i < meshes.Count; i++)
					{
						odfMesh mesh = meshes[i];
						for (int j = 0; j < mesh.Count; j++)
						{
							odfSubmesh meshObj = mesh[j];
							if (meshObj.MaterialId == mat.Id)
							{
								for (int n = 0; n < matTexNameCombo.Length; n++)
								{
									bool foundMatTex = false;
									ObjectID texID = meshObj.TextureIds[n];
									if ((int)texID == 0)
										continue;
									for (int m = 0; m < textures.Count; m++)
									{
										odfTexture tex = textures[m];
										if (texID == tex.Id)
										{
											crossRefMaterialTextures[matIdx].Add(new KeyList<odfTexture>(textures, m));
											crossRefTextureMaterials[m].Add(new KeyList<odfMaterial>(materials, matIdx));
											foundMatTex = true;
											break;
										}
									}
									if (!foundMatTex && !SuppressWarningsToolStripMenuItem.Checked)
									{
										Report.ReportLog("Warning: Couldn't find texture " + texID + " of mesh object " + meshObj.Name + ".");
									}
								}
							}
						}
					}
				}
			}

			for (int i = 0; i < meshes.Count; i++)
			{
				odfMesh mesh = meshes[i];
				for (int j = 0; j < mesh.Count; j++)
				{
					odfSubmesh meshObj = mesh[j];
					odfMaterial mat = odf.FindMaterialInfo(meshObj.MaterialId, Editor.Parser.MaterialSection);
					if (mat != null)
					{
						int matIdx = materials.IndexOf(mat);
						crossRefMeshMaterials[i].Add(new KeyList<odfMaterial>(materials, matIdx));
						crossRefMaterialMeshes[matIdx].Add(new KeyList<odfMesh>(meshes, i));
						if ((int)meshObj.MaterialId != 0 && textures != null)
						{
							for (int n = 0; n < matTexNameCombo.Length; n++)
							{
								bool foundMatTex = false;
								ObjectID texID = meshObj.TextureIds[n];
								if ((int)texID == 0)
									continue;
								for (int m = 0; m < textures.Count; m++)
								{
									odfTexture tex = textures[m];
									if (texID == tex.Id)
									{
										crossRefMeshTextures[i].Add(new KeyList<odfTexture>(textures, m));
										crossRefTextureMeshes[m].Add(new KeyList<odfMesh>(meshes, i));
										foundMatTex = true;
										break;
									}
								}
								if (!foundMatTex && !SuppressWarningsToolStripMenuItem.Checked)
								{
									Report.ReportLog("Warning: Couldn't find texture " + texID + " of mesh object " + meshObj.Name + ".");
								}
							}
						}
					}
					else if (!SuppressWarningsToolStripMenuItem.Checked)
					{
						Report.ReportLog("Warning: Mesh " + mesh.Name + " Object " + meshObj.Name + " has an invalid material id.");
					}
				}
			}

			CrossRefsSet();
		}

		private void CrossRefsSet()
		{
			listViewItemSyncSelectedSent = true;

			listViewMeshMaterial.BeginUpdate();
			listViewMeshTexture.BeginUpdate();
			for (int i = 0; i < listViewMesh.SelectedItems.Count; i++)
			{
				int mesh = (int)listViewMesh.SelectedItems[i].Tag;
				CrossRefAddItem(crossRefMeshMaterials[mesh], crossRefMeshMaterialsCount, listViewMeshMaterial, listViewMaterial);
				CrossRefAddItem(crossRefMeshTextures[mesh], crossRefMeshTexturesCount, listViewMeshTexture, listViewTexture);
			}
			listViewMeshMaterial.EndUpdate();
			listViewMeshTexture.EndUpdate();

			listViewMaterialMesh.BeginUpdate();
			listViewMaterialTexture.BeginUpdate();
			for (int i = 0; i < listViewMaterial.SelectedItems.Count; i++)
			{
				int mat = (int)listViewMaterial.SelectedItems[i].Tag;
				CrossRefAddItem(crossRefMaterialMeshes[mat], crossRefMaterialMeshesCount, listViewMaterialMesh, listViewMesh);
				CrossRefAddItem(crossRefMaterialTextures[mat], crossRefMaterialTexturesCount, listViewMaterialTexture, listViewTexture);
			}
			listViewMaterialMesh.EndUpdate();
			listViewMaterialTexture.EndUpdate();

			listViewTextureMesh.BeginUpdate();
			listViewTextureMaterial.BeginUpdate();
			for (int i = 0; i < listViewTexture.SelectedItems.Count; i++)
			{
				int tex = (int)listViewTexture.SelectedItems[i].Tag;
				CrossRefAddItem(crossRefTextureMeshes[tex], crossRefTextureMeshesCount, listViewTextureMesh, listViewMesh);
				CrossRefAddItem(crossRefTextureMaterials[tex], crossRefTextureMaterialsCount, listViewTextureMaterial, listViewMaterial);
			}
			listViewTextureMesh.EndUpdate();
			listViewTextureMaterial.EndUpdate();

			listViewItemSyncSelectedSent = false;
		}

		private void CrossRefsClear()
		{
			listViewItemSyncSelectedSent = true;

			listViewMeshMaterial.BeginUpdate();
			listViewMeshTexture.BeginUpdate();
			foreach (var pair in crossRefMeshMaterials)
			{
				int mesh = pair.Key;
				CrossRefRemoveItem(pair.Value, crossRefMeshMaterialsCount, listViewMeshMaterial);
				CrossRefRemoveItem(crossRefMeshTextures[mesh], crossRefMeshTexturesCount, listViewMeshTexture);
			}
			listViewMeshMaterial.EndUpdate();
			listViewMeshTexture.EndUpdate();

			listViewMaterialMesh.BeginUpdate();
			listViewMaterialTexture.BeginUpdate();
			foreach (var pair in crossRefMaterialMeshes)
			{
				int mat = pair.Key;
				CrossRefRemoveItem(pair.Value, crossRefMaterialMeshesCount, listViewMaterialMesh);
				CrossRefRemoveItem(crossRefMaterialTextures[mat], crossRefMaterialTexturesCount, listViewMaterialTexture);
			}
			listViewMaterialMesh.EndUpdate();
			listViewMaterialTexture.EndUpdate();

			listViewTextureMesh.BeginUpdate();
			listViewTextureMaterial.BeginUpdate();
			foreach (var pair in crossRefTextureMeshes)
			{
				int tex = pair.Key;
				CrossRefRemoveItem(pair.Value, crossRefTextureMeshesCount, listViewTextureMesh);
				CrossRefRemoveItem(crossRefTextureMaterials[tex], crossRefTextureMaterialsCount, listViewTextureMaterial);
			}
			listViewTextureMesh.EndUpdate();
			listViewTextureMaterial.EndUpdate();

			listViewItemSyncSelectedSent = false;
		}

		private void CrossRefAddItem<T>(List<KeyList<T>> list, Dictionary<int, int> dic, ListView listView, ListView mainView)
		{
			bool added = false;
			for (int i = 0; i < list.Count; i++)
			{
				int count = dic[list[i].Index] + 1;
				dic[list[i].Index] = count;
				if (count == 1)
				{
					var keylist = list[i];
					ListViewItem item = new ListViewItem(keylist.List[keylist.Index].ToString());
					item.Tag = keylist.Index;

					foreach (ListViewItem mainItem in mainView.Items)
					{
						if ((int)mainItem.Tag == keylist.Index)
						{
							item.Selected = mainItem.Selected;
							break;
						}
					}

					listView.Items.Add(item);
					added = true;
				}
			}

			if (added)
			{
				listView.AutoResizeColumns(ColumnHeaderAutoResizeStyle.ColumnContent);
			}
		}

		private void CrossRefRemoveItem<T>(List<KeyList<T>> list, Dictionary<int, int> dic, ListView listView)
		{
			bool removed = false;
			for (int i = 0; i < list.Count; i++)
			{
				int count = dic[list[i].Index] - 1;
				dic[list[i].Index] = count;
				if (count == 0)
				{
					var tuple = list[i];
					for (int j = 0; j < listView.Items.Count; j++)
					{
						if ((int)listView.Items[j].Tag == tuple.Index)
						{
							listView.Items.RemoveAt(j);
							removed = true;
							break;
						}
					}
				}
			}

			if (removed)
			{
				listView.AutoResizeColumns(ColumnHeaderAutoResizeStyle.ColumnContent);
			}
		}

		private void CrossRefSetSelected(bool selected, ListView view, int tag)
		{
			foreach (ListViewItem item in view.Items)
			{
				if ((int)item.Tag == tag)
				{
					item.Selected = selected;
					break;
				}
			}
		}

		private void listViewMeshMaterial_ItemSelectionChanged(object sender, ListViewItemSelectionChangedEventArgs e)
		{
			listViewMaterial_ItemSelectionChanged(sender, e);
		}

		private void listViewMeshTexture_ItemSelectionChanged(object sender, ListViewItemSelectionChangedEventArgs e)
		{
			listViewTexture_ItemSelectionChanged(sender, e);
		}

		private void listViewMaterialMesh_ItemSelectionChanged(object sender, ListViewItemSelectionChangedEventArgs e)
		{
			listViewMesh_ItemSelectionChanged(sender, e);
		}

		private void listViewMaterialTexture_ItemSelectionChanged(object sender, ListViewItemSelectionChangedEventArgs e)
		{
			listViewTexture_ItemSelectionChanged(sender, e);
		}

		private void listViewTextureMesh_ItemSelectionChanged(object sender, ListViewItemSelectionChangedEventArgs e)
		{
			listViewMesh_ItemSelectionChanged(sender, e);
		}

		private void listViewTextureMaterial_ItemSelectionChanged(object sender, ListViewItemSelectionChangedEventArgs e)
		{
			listViewMaterial_ItemSelectionChanged(sender, e);
		}

		#endregion Cross-Refs

		#region ObjTreeView

		private void treeViewObjectTree_AfterSelect(object sender, TreeViewEventArgs e)
		{
			if (e.Node.Tag is DragSource)
			{
				var tag = (DragSource)e.Node.Tag;
				if (tag.Type == typeof(odfFrame))
				{
					tabControlViews.SelectedTab = tabPageFrameView;
					LoadFrame((int)tag.Id);
				}
				else if (tag.Type == typeof(odfMesh))
				{
					SetListViewAfterNodeSelect(listViewMesh, tag);
				}
				else if (tag.Type == typeof(odfMaterial))
				{
					SetListViewAfterNodeSelect(listViewMaterial, tag);
				}
				else if (tag.Type == typeof(odfTexture))
				{
					SetListViewAfterNodeSelect(listViewTexture, tag);
				}
			}
			else if (e.Node.Tag is Tuple<odfBone, Tuple<int, int>>)
			{
				var tag = (Tuple<odfBone, Tuple<int, int>>)e.Node.Tag;
				tabControlViews.SelectedTab = tabPageBoneView;
				LoadBone(tag.Item2);

				if (highlightedBone != null)
					HighlightBone(highlightedBone, false);
				HighlightBone(tag.Item1, true);
				highlightedBone = tag.Item1;
			}
		}

		private void SetListViewAfterNodeSelect(ListView listView, DragSource tag)
		{
			while (listView.SelectedItems.Count > 0)
			{
				listView.SelectedItems[0].Selected = false;
			}

			for (int i = 0; i < listView.Items.Count; i++)
			{
				var item = listView.Items[i];
				if ((int)item.Tag == (int)tag.Id)
				{
					item.Selected = true;
					break;
				}
			}
		}

		private void treeViewObjectTree_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
		{
			if (e.Node.Tag is Tuple<odfBone, Tuple<int, int>> && e.Node.IsSelected)
			{
				if (highlightedBone != null)
				{
					HighlightBone(highlightedBone, false);
					highlightedBone = null;
				}
				else
				{
					highlightedBone = ((Tuple<odfBone, Tuple<int, int>>)e.Node.Tag).Item1;
					HighlightBone(highlightedBone, true);
				}
			}
		}

		private void HighlightBone(odfBone bone, bool show)
		{
			bool render = false;
			odfBoneList boneList = bone.Parent;
			for (int idx = 0; idx < Editor.Parser.MeshSection.Count; idx++)
			{
				odfMesh mesh = Editor.Parser.MeshSection[idx];
				for (int subIdx = 0; subIdx < mesh.Count; subIdx++)
				{
					if (boneList.SubmeshId != mesh[subIdx].Id)
						continue;

					RenderObjectODF renderObj = renderObjectMeshes[idx];
					if (renderObj != null)
					{
						renderObj.HighlightBone(Editor.Parser, idx, subIdx, show ? boneList.IndexOf(bone) : -1);
						render = true;
					}

					idx = Editor.Parser.MeshSection.Count;
					break;
				}
			}
			if (render)
				Gui.Renderer.Render();
		}

		TreeNode FindFrameNode(odfFrame frame, TreeNodeCollection nodes)
		{
			foreach (TreeNode node in nodes)
			{
				if (!(node.Tag is DragSource))
					continue;

				DragSource src = (DragSource)node.Tag;
				if (src.Type != typeof(odfFrame))
					continue;
				
				if ((int)src.Id == Editor.Frames.IndexOf(frame))
				{
					return node;
				}

				TreeNode found = FindFrameNode(frame, node.Nodes);
				if (found != null)
				{
					return found;
				}
			}

			return null;
		}

		TreeNode FindMeshNode(odfMesh mesh, TreeNodeCollection nodes)
		{
			foreach (TreeNode node in nodes)
			{
				if (node.Tag is DragSource)
				{
					var src =  (DragSource)node.Tag;
					if (src.Type == typeof(odfMesh) && (int)src.Id == Editor.Parser.MeshSection.IndexOf(mesh))
					{
						return node;
					}
				}

				TreeNode found = FindMeshNode(mesh, node.Nodes);
				if (found != null)
				{
					return found;
				}
			}

			return null;
		}

		TreeNode FindBoneNode(odfBone bone, TreeNodeCollection nodes)
		{
			foreach (TreeNode node in nodes)
			{
				var tuple = node.Tag as Tuple<odfBone, int[]>;
				if ((tuple != null) && tuple.Item1.Equals(bone))
				{
					return node;
				}

				TreeNode found = FindBoneNode(bone, node.Nodes);
				if (found != null)
				{
					return found;
				}
			}

			return null;
		}

		private void treeViewObjectTree_ItemDrag(object sender, ItemDragEventArgs e)
		{
			try
			{
				if (e.Item is TreeNode)
				{
					treeViewObjectTree.DoDragDrop(e.Item, DragDropEffects.Copy);
				}
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		private void treeViewObjectTree_DragEnter(object sender, DragEventArgs e)
		{
			try
			{
				UpdateDragDrop(sender, e);
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		private void treeViewObjectTree_DragOver(object sender, DragEventArgs e)
		{
			try
			{
				UpdateDragDrop(sender, e);
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		private void treeViewObjectTree_DragDrop(object sender, DragEventArgs e)
		{
			try
			{
				TreeNode node = (TreeNode)e.Data.GetData(typeof(TreeNode));
				if (node == null)
				{
					Gui.Docking.DockDragDrop(sender, e);
				}
				else
				{
					ProcessDragDropSources(node);
				}
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		private void ProcessDragDropSources(TreeNode node)
		{
			if (node.Tag is DragSource)
			{
				if ((node.Parent != null) && !node.Checked && node.StateImageIndex != (int)CheckState.Indeterminate)
				{
					return;
				}

				DragSource? dest = null;
				if (treeViewObjectTree.SelectedNode != null)
				{
					dest = treeViewObjectTree.SelectedNode.Tag as DragSource?;
				}

				DragSource source = (DragSource)node.Tag;
				if (source.Type == typeof(odfFrame))
				{
					using (var dragOptions = new FormODFDragDrop(Editor, true))
					{
						var srcEditor = (odfEditor)Gui.Scripting.Variables[source.Variable];
						var srcFrameName = srcEditor.Frames[(int)source.Id].Name;
						dragOptions.numericFrameId.Value = GetDestParentId(srcFrameName, dest);
						if (dragOptions.ShowDialog() == DialogResult.OK)
						{ // MergeFrame AddFrame ReplaceFrame
							Gui.Scripting.RunScript(EditorVar + "." + dragOptions.FrameMethod.GetName() + "(srcFrame=" + source.Variable + ".Frames[" + (int)source.Id + "], srcParser=" + source.Variable + ".Parser, destParentIdx=" + dragOptions.numericFrameId.Value
								+ (dragOptions.FrameMethod == CopyFrameMethod.ReplaceFrame ? ", deleteMorphs=" + deleteMorphsAutomaticallyToolStripMenuItem.Checked : "") + ")");
							RecreateFrames();
						}
					}
				}
				else if (source.Type == typeof(odfMaterial))
				{
					Gui.Scripting.RunScript(EditorVar + ".MergeMaterial(srcMat=" + source.Variable + ".Materials[" + (int)source.Id + "], srcParser=" + source.Variable + ".Parser)");
					RecreateMaterials();
				}
				else if (source.Type == typeof(odfTexture))
				{
					Gui.Scripting.RunScript(EditorVar + ".MergeTexture(tex=" + source.Variable + ".Textures[" + (int)source.Id + "], srcParser=" + source.Variable + ".Parser)");
					RecreateTextures();
				}
				else if (source.Type == typeof(ImportedFrame))
				{
					using (var dragOptions = new FormODFDragDrop(Editor, true))
					{
						var srcEditor = (ImportedEditor)Gui.Scripting.Variables[source.Variable];
						var srcFrameName = srcEditor.Frames[(int)source.Id].Name;
						dragOptions.numericFrameId.Value = GetDestParentId(srcFrameName, dest);
						if (dragOptions.ShowDialog() == DialogResult.OK)
						{
							Gui.Scripting.RunScript(EditorVar + "." + dragOptions.FrameMethod.GetName() + "(srcFrame=" + source.Variable + ".Frames[" + (int)source.Id + "], destParentIdx=" + dragOptions.numericFrameId.Value
								+ (dragOptions.FrameMethod == CopyFrameMethod.ReplaceFrame ? ", deleteMorphs=" + deleteMorphsAutomaticallyToolStripMenuItem.Checked : "") + ")");
							RecreateFrames();
						}
					}
				}
				else if (source.Type == typeof(WorkspaceMesh))
				{
					using (var dragOptions = new FormODFDragDrop(Editor, false))
					{
						var srcEditor = (ImportedEditor)Gui.Scripting.Variables[source.Variable];

						var destFrameIdx = Editor.GetFrameIndex(srcEditor.Imported.MeshList[(int)source.Id].Name);
						if (destFrameIdx < 0)
						{
							destFrameIdx = 0;
						}
						dragOptions.numericMeshId.Value = destFrameIdx;

						if (dragOptions.ShowDialog() == DialogResult.OK)
						{
							// repeating only final choices for repeatability of the script
							WorkspaceMesh wsMesh = srcEditor.Meshes[(int)source.Id];
							foreach (ImportedSubmesh submesh in wsMesh.SubmeshList)
							{
								if (wsMesh.isSubmeshEnabled(submesh))
								{
									if (!wsMesh.isSubmeshReplacingOriginal(submesh))
									{
										Gui.Scripting.RunScript(source.Variable + ".setSubmeshReplacingOriginal(meshId=" + (int)source.Id + ", id=" + wsMesh.SubmeshList.IndexOf(submesh) + ", replaceOriginal=false)");
									}
								}
								else
								{
									Gui.Scripting.RunScript(source.Variable + ".setSubmeshEnabled(meshId=" + (int)source.Id + ", id=" + wsMesh.SubmeshList.IndexOf(submesh) + ", enabled=false)");
								}
							}
							Gui.Scripting.RunScript(EditorVar + ".ReplaceMesh(mesh=" + source.Variable + ".Meshes[" + (int)source.Id + "], frameIdx=" + dragOptions.numericMeshId.Value +
								", materials=" + source.Variable + ".Imported.MaterialList, textures=" + source.Variable + ".Imported.TextureList, merge=" + dragOptions.radioButtonMeshMerge.Checked +
								", normals=\"" + dragOptions.NormalsMethod.GetName() + "\", bones=\"" + dragOptions.BonesMethod.GetName() + "\")");
							RecreateMeshes();
							InitMorphs();
						}
					}
				}
				else if (source.Type == typeof(ImportedMaterial))
				{
					Gui.Scripting.RunScript(EditorVar + ".MergeMaterial(mat=" + source.Variable + ".Imported.MaterialList[" + (int)source.Id + "])");
					RecreateMaterials();
				}
				else if (source.Type == typeof(ImportedTexture))
				{
					Gui.Scripting.RunScript(EditorVar + ".MergeTexture(tex=" + source.Variable + ".Imported.TextureList[" + (int)source.Id + "])");
					RecreateTextures();
				}
			}
			else
			{
				foreach (TreeNode child in node.Nodes)
				{
					ProcessDragDropSources(child);
				}
			}
		}

		private int GetDestParentId(string srcFrameName, DragSource? dest)
		{
			int destParentId = -1;
			if (dest == null)
			{
				var destFrameId = Editor.GetFrameIndex(srcFrameName);
				if (destFrameId >= 0)
				{
					var destFrameParent = Editor.Frames[destFrameId].Parent;
					if (destFrameParent != null)
					{
						for (int i = 0; i < Editor.Frames.Count; i++)
						{
							if (Editor.Frames[i] == destFrameParent)
							{
								destParentId = i;
								break;
							}
						}
					}
				}
			}
			else if (dest.Value.Type == typeof(odfFrame))
			{
				destParentId = (int)dest.Value.Id;
			}

			return destParentId;
		}

		private void UpdateDragDrop(object sender, DragEventArgs e)
		{
			Point p = treeViewObjectTree.PointToClient(new Point(e.X, e.Y));
			TreeNode target = treeViewObjectTree.GetNodeAt(p);
			if ((target != null) && ((p.X < target.Bounds.Left) || (p.X > target.Bounds.Right) || (p.Y < target.Bounds.Top) || (p.Y > target.Bounds.Bottom)))
			{
				target = null;
			}
			treeViewObjectTree.SelectedNode = target;

			TreeNode node = (TreeNode)e.Data.GetData(typeof(TreeNode));
			if (node == null)
			{
				Gui.Docking.DockDragEnter(sender, e);
			}
			else
			{
				e.Effect = e.AllowedEffect & DragDropEffects.Copy;
			}
		}

		private void buttonObjectTreeExpand_Click(object sender, EventArgs e)
		{
			try
			{
				treeViewObjectTree.BeginUpdate();
				treeViewObjectTree.ExpandAll();
				treeViewObjectTree.EndUpdate();
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		private void buttonObjectTreeCollapse_Click(object sender, EventArgs e)
		{
			try
			{
				treeViewObjectTree.BeginUpdate();
				treeViewObjectTree.CollapseAll();
				treeViewObjectTree.EndUpdate();
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		#endregion ObjTreeView

		#region MeshView

		private void listViewMesh_ItemSelectionChanged(object sender, ListViewItemSelectionChangedEventArgs e)
		{
			try
			{
				if (listViewItemSyncSelectedSent == false)
				{
					listViewItemSyncSelectedSent = true;
					listViewMeshMaterial.BeginUpdate();
					listViewMeshTexture.BeginUpdate();

					int meshIdx = (int)e.Item.Tag;
					if (e.IsSelected)
					{
						if (!Gui.Docking.DockRenderer.IsHidden)
						{
							Gui.Docking.DockRenderer.Activate();
						}
						tabControlViews.SelectedTab = tabPageMeshView;
						LoadMesh(meshIdx);
						CrossRefAddItem(crossRefMeshMaterials[meshIdx], crossRefMeshMaterialsCount, listViewMeshMaterial, listViewMaterial);
						CrossRefAddItem(crossRefMeshTextures[meshIdx], crossRefMeshTexturesCount, listViewMeshTexture, listViewTexture);

						if (renderObjectMeshes[meshIdx] == null)
						{
							odfMesh mesh = Editor.Parser.MeshSection[meshIdx];
							HashSet<int> meshIDs = new HashSet<int>() { (int)mesh.Id };
							renderObjectMeshes[meshIdx] = new RenderObjectODF(Editor.Parser, meshIDs);
						}
						RenderObjectODF renderObj = renderObjectMeshes[meshIdx];
						renderObjectIds[meshIdx] = Gui.Renderer.AddRenderObject(renderObj);
					}
					else
					{
						if (meshIdx == loadedMesh)
						{
							LoadMesh(-1);
						}
						CrossRefRemoveItem(crossRefMeshMaterials[meshIdx], crossRefMeshMaterialsCount, listViewMeshMaterial);
						CrossRefRemoveItem(crossRefMeshTextures[meshIdx], crossRefMeshTexturesCount, listViewMeshTexture);

						Gui.Renderer.RemoveRenderObject(renderObjectIds[meshIdx]);
					}

					CrossRefSetSelected(e.IsSelected, listViewMesh, meshIdx);
					CrossRefSetSelected(e.IsSelected, listViewMaterialMesh, meshIdx);
					CrossRefSetSelected(e.IsSelected, listViewTextureMesh, meshIdx);

					listViewMeshMaterial.EndUpdate();
					listViewMeshTexture.EndUpdate();
					listViewItemSyncSelectedSent = false;
				}
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		#endregion MeshView

		#region MaterialView

		private void listViewMaterial_ItemSelectionChanged(object sender, ListViewItemSelectionChangedEventArgs e)
		{
			try
			{
				if (listViewItemSyncSelectedSent == false)
				{
					listViewItemSyncSelectedSent = true;
					listViewMaterialMesh.BeginUpdate();
					listViewMaterialTexture.BeginUpdate();

					int matIdx = (int)e.Item.Tag;
					if (e.IsSelected)
					{
						tabControlViews.SelectedTab = tabPageMaterialView;
						LoadMaterial(matIdx);
						CrossRefAddItem(crossRefMaterialMeshes[matIdx], crossRefMaterialMeshesCount, listViewMaterialMesh, listViewMesh);
						CrossRefAddItem(crossRefMaterialTextures[matIdx], crossRefMaterialTexturesCount, listViewMaterialTexture, listViewTexture);
					}
					else
					{
						if (matIdx == loadedMaterial)
						{
							LoadMaterial(-1);
						}
						CrossRefRemoveItem(crossRefMaterialMeshes[matIdx], crossRefMaterialMeshesCount, listViewMaterialMesh);
						CrossRefRemoveItem(crossRefMaterialTextures[matIdx], crossRefMaterialTexturesCount, listViewMaterialTexture);
					}

					CrossRefSetSelected(e.IsSelected, listViewMaterial, matIdx);
					CrossRefSetSelected(e.IsSelected, listViewMeshMaterial, matIdx);
					CrossRefSetSelected(e.IsSelected, listViewTextureMaterial, matIdx);

					listViewMaterialMesh.EndUpdate();
					listViewMaterialTexture.EndUpdate();
					listViewItemSyncSelectedSent = false;
				}
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		#endregion MaterialView

		#region TextureView

		private void listViewTexture_ItemSelectionChanged(object sender, ListViewItemSelectionChangedEventArgs e)
		{
			try
			{
				if (listViewItemSyncSelectedSent == false)
				{
					listViewItemSyncSelectedSent = true;
					listViewTextureMesh.BeginUpdate();
					listViewTextureMaterial.BeginUpdate();

					int texIdx = (int)e.Item.Tag;
					if (e.IsSelected)
					{
						tabControlViews.SelectedTab = tabPageTextureView;
						LoadTexture(texIdx);
						CrossRefAddItem(crossRefTextureMeshes[texIdx], crossRefTextureMeshesCount, listViewTextureMesh, listViewMesh);
						CrossRefAddItem(crossRefTextureMaterials[texIdx], crossRefTextureMaterialsCount, listViewTextureMaterial, listViewMaterial);
					}
					else
					{
						if (texIdx == loadedTexture)
						{
							LoadTexture(-1);
						}
						CrossRefRemoveItem(crossRefTextureMeshes[texIdx], crossRefTextureMeshesCount, listViewTextureMesh);
						CrossRefRemoveItem(crossRefTextureMaterials[texIdx], crossRefTextureMaterialsCount, listViewTextureMaterial);
					}

					CrossRefSetSelected(e.IsSelected, listViewTexture, texIdx);
					CrossRefSetSelected(e.IsSelected, listViewMeshTexture, texIdx);
					CrossRefSetSelected(e.IsSelected, listViewMaterialTexture, texIdx);

					listViewTextureMesh.EndUpdate();
					listViewTextureMaterial.EndUpdate();
					listViewItemSyncSelectedSent = false;
				}
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		#endregion TextureView

		#region MorphView

		private void treeViewMorphObj_AfterSelect(object sender, TreeViewEventArgs e)
		{
			try
			{
/*				if (e.Node.Tag is ODF.MorphProfile)
				{
					TreeNode prevNode = prevMorphProfileNodes[e.Node.Parent.Index];
					if (prevNode != null)
					{
						mainForm.rendererView.setMorphClip(loadedSubfileXA, (MorphKeyframeRef)prevNode.Tag, 0);
						prevNode.BackColor = SystemColors.Window;
					}

					mainForm.rendererView.setMorphClip(loadedSubfileXA, (MorphKeyframeRef)e.Node.Tag, 1);
					prevMorphProfileNodes[e.Node.Parent.Index] = e.Node;
					e.Node.BackColor = SystemColors.ControlLight;
				}
				else*/
				if (e.Node.Tag is odfMorphObject)
				{
					odfMorphObject moObj = (odfMorphObject)e.Node.Tag;
					listViewMorphProfileSelection.Items.Clear();
					for (int i = 0; i < moObj.SelectorList.Count; i++)
					{
						odfMorphSelector selector = moObj.SelectorList[i];
						string name = moObj[selector.ProfileIndex].Name.ToString();
						ListViewItem item = new ListViewItem(new string[3] { selector.Threshold.ToString(), selector.ProfileIndex.ToString(), name });
						item.Tag = selector;
						listViewMorphProfileSelection.Items.Add(item);
					}
					listViewMorphProfileSelection.SelectedItems.Clear();
					numericUpDownMorphKeyframe.Value = 0;
					comboBoxMorphProfileName.Items.Clear();

					int clipType = moObj.ClipType;
					textBoxClipType.Text = clipType.ToString();
					listViewMorphUnknown.Items.Clear();
					if (clipType > 0)
					{
						List<odfMorphClip> clipList = moObj.MorphClipList;
						for (int i = 0; i < clipList.Count; i++)
						{
							if (checkBoxUnknownHideZeros.Checked && clipList[i].StartIndex == 0 && clipList[i].EndIndex == 0 && clipList[i].Unknown == 0)
								continue;
							ListViewItem item = new ListViewItem(new string[4] { i.ToString("D2"), clipList[i].StartIndex.ToString(), clipList[i].EndIndex.ToString(), clipList[i].Unknown.ToString() });
							listViewMorphUnknown.Items.Add(item);
						}
					}
					if (listViewMorphUnknown.Columns[3].Width > 50)
						listViewMorphUnknown.Columns[3].Width = 50;

					textBoxMorphObjFrameName.Text = String.Empty;
					textBoxMorphObjFrameID.Text = String.Empty;
					if ((int)moObj.FrameId != 0)
					{
						string name = odf.FindFrame(moObj.FrameId, Editor.Parser.FrameSection.RootFrame).Name;
						textBoxMorphObjFrameName.Text = name;
						textBoxMorphObjFrameID.Text = moObj.FrameId.ToString();
					}
				}
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		private void buttonMorphClipExport_Click(object sender, EventArgs e)
		{
			try
			{
				if (treeViewMorphObj.SelectedNode == null)
				{
					Report.ReportLog("No morph object was selected");
					return;
				}

				TreeNode objNode = treeViewMorphObj.SelectedNode;
				while (objNode.Parent != null)
				{
					objNode = objNode.Parent;
				}
				odfMorphObject morphObj = (odfMorphObject)objNode.Tag;

				DirectoryInfo dir = new DirectoryInfo(Path.GetDirectoryName(Editor.Parser.ODFPath) + @"\" + Path.GetFileNameWithoutExtension(Editor.Parser.ODFPath));
				Gui.Scripting.RunScript(EditorVar + ".ExportMorphObject(path=\"" + dir + "\", parser=" + EditorVar + ".Parser, morphObj=\"" + morphObj.Name + "\", skipUnusedProfiles=" + checkBoxSkipUnusedProfiles.Checked + ")");
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		private void listViewMorphProfileSelection_SelectedIndexChanged(object sender, EventArgs e)
		{
			if (listViewMorphProfileSelection.SelectedItems.Count > 0)
			{
				odfMorphSelector selector = (odfMorphSelector)listViewMorphProfileSelection.SelectedItems[0].Tag;

				numericUpDownMorphKeyframe.Value = selector.Threshold;

				TreeNode objNode = treeViewMorphObj.SelectedNode;
				while (objNode.Parent != null)
				{
					objNode = objNode.Parent;
				}
				odfMorphObject morphObj = (odfMorphObject)objNode.Tag;
				comboBoxMorphProfileName.Items.Clear();
				for (int i = 0; i < morphObj.Count; i++)
				{
					comboBoxMorphProfileName.Items.Add(morphObj[i].Name.ToString());
				}
				comboBoxMorphProfileName.SelectedIndex = selector.ProfileIndex < comboBoxMorphProfileName.Items.Count
					? selector.ProfileIndex < 0 && comboBoxMorphProfileName.Items.Count > 0 ? 0 : selector.ProfileIndex
					: comboBoxMorphProfileName.Items.Count - 1;
			}
			else
			{
				numericUpDownMorphKeyframe.Value = 0;
				comboBoxMorphProfileName.Items.Clear();
			}
		}

		// unscripted! todo: SetMorphProfile() in odfEditor
		private void buttonMorphSet_Click(object sender, EventArgs e)
		{
			if (listViewMorphProfileSelection.SelectedItems.Count == 0)
				return;

			ListViewItem item = listViewMorphProfileSelection.SelectedItems[0];
			odfMorphSelector selector = (odfMorphSelector)item.Tag;

			selector.Threshold = Decimal.ToInt32(numericUpDownMorphKeyframe.Value);
			selector.ProfileIndex = comboBoxMorphProfileName.SelectedIndex;
			string profile = (string)comboBoxMorphProfileName.Items[selector.ProfileIndex];

			listViewMorphProfileSelection.BeginUpdate();
			int index = item.Index;
			item.Remove();
			item = new ListViewItem(new string[3] { selector.Threshold.ToString(), selector.ProfileIndex.ToString(), profile });
			item.Tag = selector;
			listViewMorphProfileSelection.Items.Insert(index, item);
			item.Selected = true;
			listViewMorphProfileSelection.EndUpdate();
		}

		// unscripted! todo: AddMorphProfile() in odfEditor
		private void buttonMorphAdd_Click(object sender, EventArgs e)
		{
			odfMorphSelector newSelector = new odfMorphSelector();
			newSelector.Threshold = 0;
			newSelector.ProfileIndex = 1;

			TreeNode objNode = treeViewMorphObj.SelectedNode;
			if (objNode == null)
				return;
			while (objNode.Parent != null)
			{
				objNode = objNode.Parent;
			}
			odfMorphObject morphObj = (odfMorphObject)objNode.Tag;

			List<odfMorphSelector> selList = morphObj.SelectorList;
			int position = listViewMorphProfileSelection.SelectedItems.Count == 0 ? 0 : listViewMorphProfileSelection.SelectedIndices[0] + 1;
			selList.Insert(position, newSelector);

			string profile = morphObj[newSelector.ProfileIndex].Name.ToString(); ;
			ListViewItem item = new ListViewItem(new string[3] { newSelector.Threshold.ToString(), newSelector.ProfileIndex.ToString(), profile });
			item.Tag = newSelector;
			listViewMorphProfileSelection.Items.Insert(position, item);
		}

		// unscripted! todo: DeleteMorphProfile() in odfEditor
		private void buttonMorphDel_Click(object sender, EventArgs e)
		{
			if (listViewMorphProfileSelection.SelectedItems.Count == 0)
				return;

			ListViewItem item = listViewMorphProfileSelection.SelectedItems[0];
			odfMorphSelector selector = (odfMorphSelector)item.Tag;

			TreeNode objNode = treeViewMorphObj.SelectedNode;
			while (objNode.Parent != null)
			{
				objNode = objNode.Parent;
			}
			odfMorphObject morphObj = (odfMorphObject)objNode.Tag;

			List<odfMorphSelector> selList = morphObj.SelectorList;
			selList.Remove(selector);

			item.Tag = null;
			item.Remove();
		}

		#endregion MorphView

		#region AnimationView

		private void listViewAnimationTrack_ItemSelectionChanged(object sender, ListViewItemSelectionChangedEventArgs e)
		{
			try
			{
				odfTrack track = (odfTrack)e.Item.Tag;
				odfBone trackBone = null;
				for (int i = 0; i < Editor.Parser.EnvelopeSection.Count; i++)
				{
					odfBoneList boneList = Editor.Parser.EnvelopeSection[i];
					foreach (odfBone bone in boneList)
					{
						if (bone.FrameId == track.BoneFrameId)
						{
							trackBone = bone;
							i = Editor.Parser.EnvelopeSection.Count;
							break;
						}
					}
				}
				if (trackBone == null)
					return;

				HighlightBone(trackBone, e.IsSelected);
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		#endregion AnimationView

		#region Frame

		void textBoxFrameName_AfterEditTextChanged(object sender, EventArgs e)
		{
			try
			{
				if (loadedFrame < 0)
				{
					return;
				}

				Gui.Scripting.RunScript(EditorVar + ".SetFrameName(idx=" + loadedFrame + ", name=\"" + textBoxFrameName.Text + "\")");

				odfFrame frame = Editor.Frames[loadedFrame];
				TreeNode node = FindFrameNode(frame, treeViewObjectTree.Nodes);
				node.Text = frame.Name;
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		void textBoxFrameID_AfterEditTextChanged(object sender, EventArgs e)
		{
			try
			{
				if (loadedFrame < 0)
				{
					return;
				}

				Gui.Scripting.RunScript(EditorVar + ".SetFrameId(idx=" + loadedFrame + ", id=\"" + textBoxFrameID.Text + "\")");

				RecreateRenderObjects();
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		private void buttonFrameMoveUp_Click(object sender, EventArgs e)
		{
			try
			{
				if (loadedFrame < 0)
					return;

				odfFrame frame = Editor.Frames[loadedFrame];
				if (frame.Parent == null)
					return;
				odfFrame parentFrame = (odfFrame)frame.Parent;
				int pos = parentFrame.IndexOf(frame);
				if (pos < 1)
					return;

				TreeNode node = FindFrameNode(frame, treeViewObjectTree.Nodes);
				TreeNode parentNode = node.Parent;
				bool selected = node.Equals(node.TreeView.SelectedNode);
				int nodeIdx = node.Index;
				node.TreeView.BeginUpdate();
				parentNode.Nodes.RemoveAt(nodeIdx);
				parentNode.Nodes.Insert(nodeIdx - 1, node);
				if (selected)
				{
					node.TreeView.SelectedNode = node;
				}
				node.TreeView.EndUpdate();

				DragSource src = (DragSource)parentNode.Tag;
				Gui.Scripting.RunScript(EditorVar + ".MoveFrame(idx=" + loadedFrame + ", parent=" + (int)src.Id + ", parentDestination=" + (pos - 1) + ")");
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		private void buttonFrameMoveDown_Click(object sender, EventArgs e)
		{
			try
			{
				if (loadedFrame < 0)
					return;

				odfFrame frame = Editor.Frames[loadedFrame];
				if (frame.Parent == null)
					return;
				odfFrame parentFrame = (odfFrame)frame.Parent;
				int pos = parentFrame.IndexOf(frame);
				if (pos == parentFrame.Count - 1)
					return;

				TreeNode node = FindFrameNode(frame, treeViewObjectTree.Nodes);
				TreeNode parentNode = node.Parent;
				bool selected = node.Equals(node.TreeView.SelectedNode);
				int nodeIdx = node.Index;
				node.TreeView.BeginUpdate();
				parentNode.Nodes.RemoveAt(nodeIdx);
				parentNode.Nodes.Insert(nodeIdx + 1, node);
				if (selected)
				{
					node.TreeView.SelectedNode = node;
				}
				node.TreeView.EndUpdate();

				DragSource src = (DragSource)parentNode.Tag;
				Gui.Scripting.RunScript(EditorVar + ".MoveFrame(idx=" + loadedFrame + ", parent=" + (int)src.Id + ", parentDestination=" + (pos + 1) + ")");
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		private void buttonFrameRemove_Click(object sender, EventArgs e)
		{
			try
			{
				if (loadedFrame < 0)
					return;
				if (Editor.Frames[loadedFrame].Parent == null)
				{
					Report.ReportLog("Can't remove the root frame");
					return;
				}

				int frameIdx = loadedFrame;

				DisposeRenderObjects();
				LoadFrame(-1);
				LoadBone(null);

				Gui.Scripting.RunScript(EditorVar + ".RemoveFrame(idx=" + frameIdx + ", deleteMorphs=" + deleteMorphsAutomaticallyToolStripMenuItem.Checked + ")");

				LoadODF();
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		private void buttonFrameMatrixIdentity_Click(object sender, EventArgs e)
		{
			try
			{
				Matrix m = Matrix.Identity;
				LoadMatrix(m, dataGridViewFrameSRT, dataGridViewFrameMatrix);
				FrameMatrixApply(m);
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		private void buttonFrameMatrixCombined_Click(object sender, EventArgs e)
		{
			try
			{
				if (loadedFrame < 0)
				{
					return;
				}

				odfFrame frame = Editor.Frames[loadedFrame];
				Matrix m = Matrix.Identity;
				while (frame != null)
				{
					m *= frame.Matrix;
					frame = frame.Parent as odfFrame;
				}
				LoadMatrix(m, dataGridViewFrameSRT, dataGridViewFrameMatrix);
				FrameMatrixApply(m);
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		private void buttonFrameMatrixLocalized_Click(object sender, EventArgs e)
		{
			try
			{
				if (loadedFrame < 0)
				{
					return;
				}

				odfFrame frame = Editor.Frames[loadedFrame];
				Matrix m = Matrix.Identity;
				odfFrame parent = frame.Parent as odfFrame;
				while (parent != null)
				{
					m *= parent.Matrix;
					parent = parent.Parent as odfFrame;
				}
				m = frame.Matrix * Matrix.Invert(m);
				LoadMatrix(m, dataGridViewFrameSRT, dataGridViewFrameMatrix);
				FrameMatrixApply(m);
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		private void buttonFrameMatrixInverse_Click(object sender, EventArgs e)
		{
			try
			{
				Matrix m = Matrix.Invert(GetMatrix(dataGridViewFrameMatrix));
				LoadMatrix(m, dataGridViewFrameSRT, dataGridViewFrameMatrix);
				FrameMatrixApply(m);
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		private void dataGridViewFrameSRT_CellValueChanged(object sender, DataGridViewCellEventArgs e)
		{
			Vector3[] srt = GetSRT(dataGridViewFrameSRT);
			Matrix m = FbxUtility.SRTToMatrix(srt[0], srt[1], srt[2]);
			LoadMatrix(m, dataGridViewFrameSRT, dataGridViewFrameMatrix);
			FrameSRTApply(srt);
		}

		private void FrameSRTApply(Vector3[] srt)
		{
			try
			{
				if (loadedFrame < 0)
				{
					return;
				}

				string command = EditorVar + ".SetFrameSRT(idx=" + loadedFrame;
				char[] argPrefix = new char[3] { 's', 'r', 't' };
				char[] argAxis = new char[3] { 'X', 'Y', 'Z' };
				for (int i = 0; i < 3; i++)
				{
					for (int j = 0; j < 3; j++)
					{
						command += ", " + argPrefix[i] + argAxis[j] + "=" + srt[i][j].ToFloatString();
					}
				}
				command += ")";

				Gui.Scripting.RunScript(command);
				RecreateRenderObjects();
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		private void dataGridViewFrameMatrix_CellValueChanged(object sender, DataGridViewCellEventArgs e)
		{
			Matrix m = GetMatrix(dataGridViewFrameMatrix);
			LoadMatrix(m, dataGridViewFrameSRT, dataGridViewFrameMatrix);
			FrameMatrixApply(m);
		}

		private void FrameMatrixApply(Matrix m)
		{
			try
			{
				if (loadedFrame < 0)
				{
					return;
				}

				string command = EditorVar + ".SetFrameMatrix(idx=" + loadedFrame;
				for (int i = 0; i < 4; i++)
				{
					for (int j = 0; j < 4; j++)
					{
						command += ", m" + (i + 1) + (j + 1) + "=" + m[i, j].ToFloatString();
					}
				}
				command += ")";

				Gui.Scripting.RunScript(command);
				RecreateRenderObjects();
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		#endregion Frame

		Matrix GetMatrix(DataGridView viewMatrix)
		{
			Matrix m = new Matrix();
			DataTable table = (DataTable)viewMatrix.DataSource;
			for (int i = 0; i < 4; i++)
			{
				for (int j = 0; j < 4; j++)
				{
					m[i, j] = (float)table.Rows[i][j];
				}
			}
			return m;
		}

		Vector3[] GetSRT(DataGridView viewSRT)
		{
			DataTable table = (DataTable)viewSRT.DataSource;
			Vector3[] srt = new Vector3[3];
			for (int i = 0; i < 3; i++)
			{
				srt[0][i] = (float)table.Rows[2][i + 1];
				srt[1][i] = (float)table.Rows[1][i + 1];
				srt[2][i] = (float)table.Rows[0][i + 1];
			}
			return srt;
		}

		#region Bone

		void textBoxBoneFrameID_AfterEditTextChanged(object sender, EventArgs e)
		{
			try
			{
				if (loadedBone == null)
				{
					return;
				}

				Gui.Scripting.RunScript(EditorVar + ".SetBoneFrameId(boneListIdx= " + loadedBone.Item1 + ", boneIdx=" + loadedBone.Item2 + ", frameId=\"" + textBoxBoneFrameID.Text + "\")");

				LoadBone(loadedBone);
				InitFrames();
				RecreateRenderObjects();
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		private void buttonBoneGotoFrame_Click(object sender, EventArgs e)
		{
			try
			{
				if (loadedBone != null)
				{
					odfBone bone = Editor.Parser.EnvelopeSection[loadedBone.Item1][loadedBone.Item2];
					odfFrame boneFrame = odf.FindFrame(bone.FrameId, Editor.Parser.FrameSection.RootFrame);
					TreeNode node = FindFrameNode(boneFrame, treeViewObjectTree.Nodes);
					if (node != null)
					{
						tabControlLists.SelectedTab = tabPageObject;
						treeViewObjectTree.SelectedNode = node;
						node.Expand();
						node.EnsureVisible();
					}
				}
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		private void buttonBoneRemove_Click(object sender, EventArgs e)
		{
			try
			{
				if (loadedBone == null)
				{
					return;
				}

				Gui.Scripting.RunScript(EditorVar + ".RemoveBone(boneListIdx=" + loadedBone.Item1 + ", boneIdx=" + loadedBone.Item2 + ")");

				LoadBone(null);
				InitFrames();
				highlightedBone = null;
				RecreateRenderObjects();
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		private void buttonBoneCopy_Click(object sender, EventArgs e)
		{
			try
			{
				if (loadedBone == null)
				{
					return;
				}

				Gui.Scripting.RunScript(EditorVar + ".CopyBone(boneListIdx=" + loadedBone.Item1 + ", boneIdx=" + loadedBone.Item2 + ")");

				InitFrames();
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		private void dataGridViewBoneSRT_CellValueChanged(object sender, DataGridViewCellEventArgs e)
		{
			Vector3[] srt = GetSRT(dataGridViewBoneSRT);
			Matrix m = FbxUtility.SRTToMatrix(srt[0], srt[1], srt[2]);
			LoadMatrix(m, dataGridViewBoneSRT, dataGridViewBoneMatrix);
			BoneSRTApply(srt);
		}

		private void BoneSRTApply(Vector3[] srt)
		{
			try
			{
				if (loadedBone == null)
				{
					return;
				}

				string command = EditorVar + ".SetBoneSRT(boneListIdx=" + loadedBone.Item1 + ", boneIdx=" + loadedBone.Item2;
				char[] argPrefix = new char[3] { 's', 'r', 't' };
				char[] argAxis = new char[3] { 'X', 'Y', 'Z' };
				for (int i = 0; i < 3; i++)
				{
					for (int j = 0; j < 3; j++)
					{
						command += ", " + argPrefix[i] + argAxis[j] + "=" + srt[i][j].ToFloatString();
					}
				}
				command += ")";

				Gui.Scripting.RunScript(command);
				RecreateRenderObjects();
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		private void dataGridViewBoneMatrix_CellValueChanged(object sender, DataGridViewCellEventArgs e)
		{
			Matrix m = GetMatrix(dataGridViewBoneMatrix);
			LoadMatrix(m, dataGridViewBoneSRT, dataGridViewBoneMatrix);
			BoneMatrixApply(m);
		}

		private void BoneMatrixApply(Matrix m)
		{
			try
			{
				if (loadedBone == null)
				{
					return;
				}

				string command = EditorVar + ".SetBoneMatrix(boneListIdx=" + loadedBone.Item1 + ", boneIdx=" + loadedBone.Item2;
				for (int i = 0; i < 4; i++)
				{
					for (int j = 0; j < 4; j++)
					{
						command += ", m" + (i + 1) + (j + 1) + "=" + m[i, j].ToFloatString();
					}
				}
				command += ")";
				Gui.Scripting.RunScript(command);

				RecreateRenderObjects();
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		#endregion Bone

		#region Mesh

		void textBoxMeshName_AfterEditTextChanged(object sender, EventArgs e)
		{
			try
			{
				if (loadedMesh < 0)
				{
					return;
				}

				odfMesh mesh = Editor.Parser.MeshSection[loadedMesh];
				if (mesh.Name != textBoxMeshName.Text)
				{
					Gui.Scripting.RunScript(EditorVar + ".SetMeshName(idx=" + loadedMesh + ", name=\"" + textBoxMeshName.Text + "\")");

					string meshName = mesh.ToString();

					TreeNode node = FindMeshNode(mesh, treeViewObjectTree.Nodes);
					node.Text = meshName;

					RenameListViewItems(Editor.Parser.MeshSection.ChildList, listViewMesh, mesh, meshName);
					RenameListViewItems(Editor.Parser.MeshSection.ChildList, listViewMaterialMesh, mesh, meshName);
					RenameListViewItems(Editor.Parser.MeshSection.ChildList, listViewTextureMesh, mesh, meshName);
					InitMorphs();
				}
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		void textBoxMeshID_AfterEditTextChanged(object sender, EventArgs e)
		{
			try
			{
				if (loadedMesh < 0)
				{
					return;
				}

				Gui.Scripting.RunScript(EditorVar + ".SetMeshId(idx=" + loadedMesh + ", id=\"" + textBoxMeshID.Text + "\")");

				odfMesh mesh = Editor.Parser.MeshSection[loadedMesh];
				if (mesh.Name.Name == String.Empty)
				{
					string meshName = mesh.Id.ToString();

					TreeNode node = FindMeshNode(mesh, treeViewObjectTree.Nodes);
					node.Text = meshName;

					RenameListViewItems(Editor.Parser.MeshSection.ChildList, listViewMesh, mesh, meshName);
					RenameListViewItems(Editor.Parser.MeshSection.ChildList, listViewMaterialMesh, mesh, meshName);
					RenameListViewItems(Editor.Parser.MeshSection.ChildList, listViewTextureMesh, mesh, meshName);
				}

				RecreateRenderObjects();
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		void textBoxMeshInfo_AfterEditTextChanged(object sender, EventArgs e)
		{
			Gui.Scripting.RunScript(EditorVar + ".SetMeshInfo(idx=" + loadedMesh + ", info=\"" + textBoxMeshInfo.Text + "\")");
		}

		private void buttonMeshExport_Click(object sender, EventArgs e)
		{
			try
			{
				DirectoryInfo dir = new DirectoryInfo(exportDir);

				string meshNames = String.Empty;
				if (listViewMesh.SelectedItems.Count > 0)
				{
					for (int i = 0; i < listViewMesh.SelectedItems.Count; i++)
					{
						meshNames += "\"" + Editor.Parser.MeshSection[(int)listViewMesh.SelectedItems[i].Tag].Name + "\", ";
					}
				}
				else
				{
					if (listViewMesh.Items.Count <= 0)
					{
						Report.ReportLog("There are no meshes for exporting");
						return;
					}

					for (int i = 0; i < listViewMesh.Items.Count; i++)
					{
						meshNames += "\"" + Editor.Parser.MeshSection[(int)listViewMesh.Items[i].Tag].Name + "\", ";
					}
				}
				meshNames = "{ " + meshNames.Substring(0, meshNames.Length - 2) + " }";

				Report.ReportLog("Started exporting to " + comboBoxMeshExportFormat.SelectedItem + " format...");
				Application.DoEvents();

				switch ((MeshExportFormat)comboBoxMeshExportFormat.SelectedIndex)
				{
				case MeshExportFormat.Mqo:
					Gui.Scripting.RunScript("ExportMqo(parser=" + ParserVar + ", meshNames=" + meshNames + ", dirPath=\"" + dir.FullName + "\", singleMqo=" + checkBoxMeshExportMqoSingleFile.Checked + ", worldCoords=" + checkBoxMeshExportMqoWorldCoords.Checked + ")");
					break;
				case MeshExportFormat.ColladaFbx:
					Gui.Scripting.RunScript("ExportODFtoFbx(parser=" + ParserVar + ", meshNames=" + meshNames + ", path=\"" + Utility.GetDestFile(dir, "meshes", ".dae") + "\", exportFormat=\".dae\", allFrames=" + checkBoxMeshExportFbxAllFrames.Checked + ", skins=" + checkBoxMeshExportFbxSkins.Checked + ", _8dot3=" + checkBox8dot3.Checked + ")");
					break;
				case MeshExportFormat.Fbx:
					Gui.Scripting.RunScript("ExportODFtoFbx(parser=" + ParserVar + ", meshNames=" + meshNames + ", path=\"" + Utility.GetDestFile(dir, "meshes", ".fbx") + "\", exportFormat=\".fbx\", allFrames=" + checkBoxMeshExportFbxAllFrames.Checked + ", skins=" + checkBoxMeshExportFbxSkins.Checked + ", _8dot3=" + checkBox8dot3.Checked + ")");
					break;
				case MeshExportFormat.Dxf:
					Gui.Scripting.RunScript("ExportODFtoFbx(parser=" + ParserVar + ", meshNames=" + meshNames + ", path=\"" + Utility.GetDestFile(dir, "meshes", ".dxf") + "\", exportFormat=\".dxf\", allFrames=" + checkBoxMeshExportFbxAllFrames.Checked + ", skins=" + checkBoxMeshExportFbxSkins.Checked + ", _8dot3=" + checkBox8dot3.Checked + ")");
					break;
				case MeshExportFormat._3ds:
					Gui.Scripting.RunScript("ExportODFtoFbx(parser=" + ParserVar + ", meshNames=" + meshNames + ", path=\"" + Utility.GetDestFile(dir, "meshes", ".3ds") + "\", exportFormat=\".3ds\", allFrames=" + checkBoxMeshExportFbxAllFrames.Checked + ", skins=" + checkBoxMeshExportFbxSkins.Checked + ", _8dot3=" + checkBox8dot3.Checked + ")");
					break;
				case MeshExportFormat.Obj:
					Gui.Scripting.RunScript("ExportODFtoFbx(parser=" + ParserVar + ", meshNames=" + meshNames + ", path=\"" + Utility.GetDestFile(dir, "meshes", ".obj") + "\", exportFormat=\".obj\", allFrames=" + checkBoxMeshExportFbxAllFrames.Checked + ", skins=" + checkBoxMeshExportFbxSkins.Checked + ", _8dot3=" + checkBox8dot3.Checked + ")");
					break;
				default:
					throw new Exception("Unexpected ExportFormat");
				}
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		private void comboBoxMeshExportFormat_SelectedIndexChanged(object sender, EventArgs e)
		{
			try
			{
				switch ((MeshExportFormat)comboBoxMeshExportFormat.SelectedIndex)
				{
				case MeshExportFormat.Mqo:
					panelMeshExportOptionsMqo.BringToFront();
					break;
				case MeshExportFormat.Fbx:
				case MeshExportFormat.ColladaFbx:
				case MeshExportFormat.Dxf:
				case MeshExportFormat._3ds:
				case MeshExportFormat.Obj:
					panelMeshExportOptionsFbx.BringToFront();
					break;
				default:
					panelMeshExportOptionsDefault.BringToFront();
					break;
				}
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		private void buttonMeshGotoFrame_Click(object sender, EventArgs e)
		{
			try
			{
				if (loadedMesh >= 0)
				{
					TreeNode node = FindMeshNode(Editor.Parser.MeshSection[loadedMesh], treeViewObjectTree.Nodes);
					if (node != null)
					{
						node = node.Parent;
						tabControlLists.SelectedTab = tabPageObject;
						treeViewObjectTree.SelectedNode = node;
						node.Expand();
						node.EnsureVisible();
					}
				}
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		private void buttonMeshRemove_Click(object sender, EventArgs e)
		{
			try
			{
				if (loadedMesh < 0)
				{
					return;
				}

				Gui.Scripting.RunScript(EditorVar + ".RemoveMesh(idx=" + loadedMesh + ", deleteMorphs=" + deleteMorphsAutomaticallyToolStripMenuItem.Checked + ")");

				RecreateMeshes();
				if (deleteMorphsAutomaticallyToolStripMenuItem.Checked)
					InitMorphs();
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		private void buttonMeshNormals_Click(object sender, EventArgs e)
		{
			try
			{
				if (loadedMesh < 0)
				{
					return;
				}

				using (var normals = new FormXXNormals())
				{
					if (normals.ShowDialog() == DialogResult.OK)
					{
						Gui.Scripting.RunScript(EditorVar + ".CalculateNormals(idx=" + loadedMesh + ", threshold=" + normals.numericThreshold.Value + ")");

						RecreateRenderObjects();
					}
				}
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		void textBoxMeshObjName_AfterEditTextChanged(object sender, EventArgs e)
		{
			try
			{
				if (loadedMesh < 0 || dataGridViewMesh.SelectedRows.Count != 1)
				{
					return;
				}

				DataGridViewRow row = dataGridViewMesh.SelectedRows[0];
				odfSubmesh submesh = (odfSubmesh)row.Tag;
				if (submesh.Name.Name != textBoxMeshObjName.Text)
				{
					Gui.Scripting.RunScript(EditorVar + ".SetSubmeshName(meshIdx=" + loadedMesh + ", submeshIdx=" + row.Index + ", name=\"" + textBoxMeshObjName.Text + "\")");

					InitFrames();
					row.Cells[0].Value = submesh.Name.Name;
					InitMorphs();
				}
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		void textBoxMeshObjID_AfterEditTextChanged(object sender, EventArgs e)
		{
			try
			{
				if (loadedMesh < 0 || dataGridViewMesh.SelectedRows.Count != 1)
				{
					return;
				}

				DataGridViewRow row = dataGridViewMesh.SelectedRows[0];
				odfSubmesh submesh = (odfSubmesh)row.Tag;
				if (submesh.Id.ToString() != textBoxMeshObjID.Text)
				{
					Gui.Scripting.RunScript(EditorVar + ".SetSubmeshId(meshIdx=" + loadedMesh + ", submeshIdx=" + row.Index + ", id=\"" + textBoxMeshObjID.Text + "\")");

					InitFrames();
					InitMorphs();
				}
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		void textBoxMeshObjInfo_AfterEditTextChanged(object sender, EventArgs e)
		{
			try
			{
				if (loadedMesh < 0 || dataGridViewMesh.SelectedRows.Count != 1)
				{
					return;
				}

				DataGridViewRow row = dataGridViewMesh.SelectedRows[0];
				odfSubmesh submesh = (odfSubmesh)row.Tag;
				if (submesh.Name.Info.ToString() != textBoxMeshObjInfo.Text)
				{
					Gui.Scripting.RunScript(EditorVar + ".SetSubmeshInfo(meshIdx=" + loadedMesh + ", submeshIdx=" + row.Index + ", info=\"" + textBoxMeshObjInfo.Text + "\")");
				}
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		private void buttonMeshObjEdit_Click(object sender, EventArgs e)
		{
			Report.ReportLog("submesh edit - unimplemented");
		}

		private void buttonMeshObjRemove_Click(object sender, EventArgs e)
		{
			try
			{
				if ((loadedMesh < 0) || (dataGridViewMesh.SelectedRows.Count <= 0))
				{
					return;
				}

				dataGridViewMesh.SelectionChanged -= new EventHandler(dataGridViewMesh_SelectionChanged);

				int lastSelectedRow = -1;
				List<int> indices = new List<int>();
				foreach (DataGridViewRow row in dataGridViewMesh.SelectedRows)
				{
					indices.Add(row.Index);
					lastSelectedRow = row.Index;
				}
				indices.Sort();

				bool meshRemoved = (indices.Count == Editor.Parser.MeshSection[loadedMesh].Count);

				for (int i = 0; i < indices.Count; i++)
				{
					int index = indices[i] - i;
					Gui.Scripting.RunScript(EditorVar + ".RemoveSubmesh(meshIdx=" + loadedMesh + ", submeshIdx=" + index + ", deleteMorphs=" + deleteMorphsAutomaticallyToolStripMenuItem.Checked + ")");
				}

				dataGridViewMesh.SelectionChanged += new EventHandler(dataGridViewMesh_SelectionChanged);

				if (meshRemoved)
				{
					RecreateMeshes();
				}
				else
				{
					LoadMesh(loadedMesh);
					if (lastSelectedRow == dataGridViewMesh.Rows.Count)
						lastSelectedRow--;
					dataGridViewMesh.Rows[lastSelectedRow].Selected = true;
					dataGridViewMesh.FirstDisplayedScrollingRowIndex = lastSelectedRow;
					RecreateRenderObjects();
					RecreateCrossRefs();
				}
				if (deleteMorphsAutomaticallyToolStripMenuItem.Checked)
					InitMorphs();
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		private void dataGridViewMesh_SelectionChanged(object sender, EventArgs e)
		{
			try
			{
				DataGridView thisDataGridView = (DataGridView)sender;
				if (thisDataGridView.SelectedRows.Count == 1)
				{
					foreach (DataGridViewRow row in thisDataGridView.SelectedRows)
					{
						odfSubmesh submesh = (odfSubmesh)row.Tag;
						textBoxMeshObjName.Text = submesh.Name;
						textBoxMeshObjInfo.Text = submesh.Name.Info;
						textBoxMeshObjID.Text = submesh.Id.ToString();
						checkBoxMeshObjSkinned.Checked = odf.FindBoneList(submesh.Id, Editor.Parser.EnvelopeSection) != null;
						break;
					}
				}
				else
				{
					textBoxMeshObjName.Text = String.Empty;
					textBoxMeshObjInfo.Text = String.Empty;
					textBoxMeshObjID.Text = String.Empty;
				}

				HighlightSubmeshes();
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		void HighlightSubmeshes()
		{
			if (loadedMesh < 0)
			{
				return;
			}

			RenderObjectODF renderObj = renderObjectMeshes[loadedMesh];
			if (renderObj != null)
			{
				renderObj.HighlightSubmesh.Clear();
				foreach (DataGridViewRow row in dataGridViewMesh.SelectedRows)
				{
					renderObj.HighlightSubmesh.Add(row.Index);
				}
				Gui.Renderer.Render();
			}
		}

		private void dataGridViewMesh_DataError(object sender, DataGridViewDataErrorEventArgs e)
		{
			e.Cancel = true;
		}

		// http://connect.microsoft.com/VisualStudio/feedback/details/151567/datagridviewcomboboxcell-needs-selectedindexchanged-event
		private void dataGridViewMesh_EditingControlShowing(object sender, DataGridViewEditingControlShowingEventArgs e)
		{
			try
			{
				if (!SetComboboxEvent)
				{
					if (e.Control.GetType() == typeof(DataGridViewComboBoxEditingControl))
					{
						ComboBox comboBoxCell = (ComboBox)e.Control;
						if (comboBoxCell != null)
						{
							//Remove an existing event-handler, if present, to avoid
							//adding multiple handlers when the editing control is reused.
							comboBoxCell.SelectedIndexChanged -= new EventHandler(comboBoxCell_SelectedIndexChanged);

							//Add the event handler.
							comboBoxCell.SelectedIndexChanged += new EventHandler(comboBoxCell_SelectedIndexChanged);
							SetComboboxEvent = true;
						}
					}
				}
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		private void comboBoxCell_SelectedIndexChanged(object sender, EventArgs e)
		{
			try
			{
				ComboBox combo = (ComboBox)sender;

				combo.SelectedIndexChanged -= new EventHandler(comboBoxCell_SelectedIndexChanged);
				SetComboboxEvent = false;

				Tuple<string, int> comboValue = (Tuple<string, int>)combo.SelectedItem;
				if (comboValue == null)
				{
					return;
				}

				int currentCellValueBeforeEndEdit = (int)dataGridViewMesh.CurrentCell.Value;

				dataGridViewMesh.EndEdit();

				int matIdValue = comboValue.Item2;
				if (matIdValue != currentCellValueBeforeEndEdit)
				{
					int rowIdx = dataGridViewMesh.CurrentCell.RowIndex;
					odfSubmesh submesh = Editor.Parser.MeshSection[loadedMesh][rowIdx];

					ObjectID newId = new ObjectID(BitConverter.GetBytes(matIdValue));
					Gui.Scripting.RunScript(EditorVar + ".SetSubmeshMaterial(meshIdx=" + loadedMesh + ", submeshIdx=" + rowIdx + ", matId=\"" + newId + "\")");

					RecreateRenderObjects();
					RecreateCrossRefs();

					dataGridViewMesh.CurrentCell.Value = matIdValue;
				}
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		#endregion Mesh

		#region Material

		void textBoxMatName_AfterEditTextChanged(object sender, EventArgs e)
		{
			try
			{
				if (loadedMaterial < 0)
				{
					return;
				}

				odfMaterial mat = Editor.Parser.MaterialSection[loadedMaterial];
				if (mat.Name.Name != textBoxMatName.Text)
				{
					Gui.Scripting.RunScript(EditorVar + ".SetMaterialName(idx=" + loadedMaterial + ", name=\"" + textBoxMatName.Text + "\")");

					InitMaterials();
					RenameListViewItems(Editor.Parser.MaterialSection.ChildList, listViewMeshMaterial, mat, mat.Name);
					RenameListViewItems(Editor.Parser.MaterialSection.ChildList, listViewTextureMaterial, mat, mat.Name);
				}
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		private void textBoxMatID_AfterEditTextChanged(object sender, EventArgs e)
		{
			try
			{
				if (loadedMaterial < 0)
				{
					return;
				}

				odfMaterial mat = Editor.Parser.MaterialSection[loadedMaterial];
				if (mat.Id.ToString() != textBoxMatID.Text)
				{
					Gui.Scripting.RunScript(EditorVar + ".SetMaterialId(idx=" + loadedMaterial + ", id=\"" + textBoxMatID.Text + "\")");
				}
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		private void comboBoxMatTexMeshObj_SelectedIndexChanged(object sender, EventArgs e)
		{
			setMaterialViewTextures();
		}

		private void comboBoxMatSetSelector_SelectedIndexChanged(object sender, EventArgs e)
		{
			setMaterialViewProperties(comboBoxMatSetSelector.SelectedIndex, (int)comboBoxMatSetSelector.Tag);
		}

		void matTexNameCombo_SelectedIndexChanged(object sender, EventArgs e)
		{
			try
			{
				if (loadedMaterial < 0)
				{
					return;
				}

				odfSubmesh submesh = (odfSubmesh)comboBoxMatTexMeshObj.SelectedItem;
				odfMesh mesh = submesh.Parent;
				int meshIdx = Editor.Parser.MeshSection.IndexOf(mesh);
				int submeshIdx = mesh.IndexOf(submesh);

				ComboBox combo = (ComboBox)sender;
				int matTexIdx = (int)combo.Tag;
				string texId = (combo.SelectedIndex == 0) ? String.Empty : ((odfTexture)combo.SelectedItem).Id.ToString();

				Gui.Scripting.RunScript(EditorVar + ".SetSubmeshTexture(meshIdx=" + meshIdx + ", submeshIdx=" + submeshIdx + ", texIdx=" + matTexIdx + ", texId=\"" + texId + "\")");

				RecreateRenderObjects();
				RecreateCrossRefs();
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		void matMatrixText_AfterEditTextChanged(object sender, EventArgs e)
		{
			try
			{
				if (loadedMaterial < 0)
				{
					return;
				}

				odfMaterial mat = Editor.Parser.MaterialSection[loadedMaterial];
				Gui.Scripting.RunScript(EditorVar + ".SetMaterialPhong(origin=" + comboBoxMatSetSelector.SelectedIndex +
					", idx=" + loadedMaterial +
					", diffuse=" + MatMatrixColorScript(matMatrixText[0]) +
					", ambient=" + MatMatrixColorScript(matMatrixText[1]) +
					", emissive=" + MatMatrixColorScript(matMatrixText[3]) +
					", specular=" + MatMatrixColorScript(matMatrixText[2]) +
					", shininess=" + Single.Parse(matMatrixText[4][0].Text).ToFloatString() +
					", unknown=" + Single.Parse(matMatrixText[4][1].Text).ToFloatString() + ")");

				RecreateRenderObjects();
				RecreateCrossRefs();
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		string MatMatrixColorScript(EditTextBox[] textBoxes)
		{
			return "{" +
				Single.Parse(textBoxes[0].Text).ToFloatString() + "," +
				Single.Parse(textBoxes[1].Text).ToFloatString() + "," +
				Single.Parse(textBoxes[2].Text).ToFloatString() + "," +
				Single.Parse(textBoxes[3].Text).ToFloatString() + "}";
		}

		private void buttonMaterialRemove_Click(object sender, EventArgs e)
		{
			try
			{
				if (loadedMaterial < 0)
				{
					return;
				}

				Gui.Scripting.RunScript(EditorVar + ".RemoveMaterial(idx=" + loadedMaterial + ")");

				RecreateRenderObjects();
				InitMaterials();
				RecreateCrossRefs();
				LoadMesh(loadedMesh);
				LoadMaterial(-1);
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		private void buttonMaterialCopy_Click(object sender, EventArgs e)
		{
			try
			{
				if (loadedMaterial < 0)
				{
					return;
				}

				Gui.Scripting.RunScript(EditorVar + ".CopyMaterial(idx=" + loadedMaterial + ")");

				InitMaterials();
				RecreateCrossRefs();
				LoadMesh(loadedMesh);
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		#endregion Material

		#region Texture

		void textBoxTexName_AfterEditTextChanged(object sender, EventArgs e)
		{
			try
			{
				if (loadedTexture < 0)
				{
					return;
				}

				Gui.Scripting.RunScript(EditorVar + ".SetTextureName(idx=" + loadedTexture + ", name=\"" + textBoxTexName.Text + "\")");

				InitTextures();
				odfTexture tex = Editor.Parser.TextureSection[loadedTexture];
				RenameListViewItems(Editor.Parser.TextureSection.ChildList, listViewMeshTexture, tex, tex.Name);
				RenameListViewItems(Editor.Parser.TextureSection.ChildList, listViewMaterialTexture, tex, tex.Name);
				setMaterialViewTextures();
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		void textBoxTexID_AfterEditTextChanged(object sender, EventArgs e)
		{
			try
			{
				if (loadedTexture < 0)
				{
					return;
				}

				Gui.Scripting.RunScript(EditorVar + ".SetTextureId(idx=" + loadedTexture + ", id=\"" + textBoxTexID.Text + "\")");
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		private void buttonTextureDecrypt_Click(object sender, EventArgs e)
		{
			try
			{
				if (listViewTexture.SelectedIndices.Count <= 0)
				{
					Report.ReportLog("No textures are selected for decryption");
					return;
				}

				for (int i = 0; i < listViewTexture.SelectedItems.Count; i++)
				{
					int texIdx = (int)listViewTexture.SelectedItems[i].Tag;
					Gui.Scripting.RunScript(EditorVar + ".DecryptTexture(idx=" + texIdx + ")");
				}
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		private void buttonTextureRemove_Click(object sender, EventArgs e)
		{
			try
			{
				if (loadedTexture < 0)
				{
					return;
				}

				Gui.Scripting.RunScript(EditorVar + ".RemoveTexture(idx=" + loadedTexture + ")");

				RecreateRenderObjects();
				InitTextures();
				RecreateCrossRefs();
				LoadMaterial(loadedMaterial);
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		private void buttonTextureAdd_Click(object sender, EventArgs e)
		{
			try
			{
				if (Gui.ImageControl.Image == null)
				{
					Report.ReportLog("An image hasn't been loaded");
					return;
				}

				Gui.Scripting.RunScript(EditorVar + ".AddTexture(image=" + Gui.ImageControl.ImageScriptVariable + ")");

				RecreateRenderObjects();
				InitTextures();
				RecreateCrossRefs();
				LoadMaterial(loadedMaterial);
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		private void buttonTextureReplace_Click(object sender, EventArgs e)
		{
			try
			{
				if (loadedTexture < 0)
				{
					return;
				}
				if (Gui.ImageControl.Image == null)
				{
					Report.ReportLog("An image hasn't been loaded");
					return;
				}

				Gui.Scripting.RunScript(EditorVar + ".ReplaceTexture(idx=" + loadedTexture + ", image=" + Gui.ImageControl.ImageScriptVariable + ")");

				RecreateRenderObjects();
				InitTextures();
				RecreateCrossRefs();
				LoadMaterial(loadedMaterial);
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		#endregion Texture

		#region Menu Strip Item Handlers

		private void reopenToolStripMenuItem_Click(object sender, EventArgs e)
		{
			try
			{
				string opensFileVar = Gui.Scripting.GetNextVariable("opensODF");
				Gui.Scripting.RunScript(opensFileVar + " = FormMeshView(path=\"" + Editor.Parser.ODFPath + "\", variable=\"" + opensFileVar + "\")", false);
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		private void saveODFToolStripMenuItem_Click(object sender, EventArgs e)
		{
			Gui.Scripting.RunScript(EditorVar + ".SaveODF(keepBackup=" + keepBackupToolStripMenuItem.Checked + ")");
		}

		private void saveODFAsToolStripMenuItem_Click(object sender, EventArgs e)
		{
			string path = ((odfParser)Gui.Scripting.Variables[ParserVar]).ODFPath;
			saveFileDialog1.InitialDirectory = Path.GetDirectoryName(path);
			saveFileDialog1.FileName = Path.GetFileNameWithoutExtension(path);
			if (saveFileDialog1.ShowDialog() == DialogResult.OK)
			{
				Gui.Scripting.RunScript(EditorVar + ".SaveODF(path=\"" + saveFileDialog1.FileName + "\", keepBackup=" + keepBackupToolStripMenuItem.Checked + ")");
			}
		}

		private void closeToolStripMenuItem_Click(object sender, EventArgs e)
		{
			try
			{
				Close();
			}
			catch (Exception ex)
			{
				Utility.ReportException(ex);
			}
		}

		private void closeViewFilesAtStartToolStripMenuItem_CheckedChanged(object sender, EventArgs e)
		{
			Properties.Settings.Default.CloseViewFilesAtStart = closeViewFilesAtStartToolStripMenuItem.Checked;
		}

		private void closeViewFilesAtStartToolStripMenuItem_Click(object sender, EventArgs e)
		{
			closeViewFilesAtStartToolStripMenuItem.Checked ^= true;
		}

		private void SuppressWarningsToolStripMenuItem_CheckedChanged(object sender, EventArgs e)
		{
			Properties.Settings.Default.SuppressWarnings = SuppressWarningsToolStripMenuItem.Checked;
		}

		private void SuppressWarningsToolStripMenuItem_Click(object sender, EventArgs e)
		{
			SuppressWarningsToolStripMenuItem.Checked ^= true;
		}

		private void keepBackupToolStripMenuItem_CheckedChanged(object sender, EventArgs e)
		{
			Properties.Settings.Default.KeepBackupOfODFs = keepBackupToolStripMenuItem.Checked;
		}

		private void keepBackupToolStripMenuItem_Click(object sender, EventArgs e)
		{
			keepBackupToolStripMenuItem.Checked ^= true;
		}

		private void deleteMorphsAutomaticallyToolStripMenuItem_CheckedChanged(object sender, EventArgs e)
		{
			Properties.Settings.Default.DeleteMorphsAutomatically = deleteMorphsAutomaticallyToolStripMenuItem.Checked;
		}

		private void deleteMorphsAutomaticallyToolStripMenuItem_Click(object sender, EventArgs e)
		{
			deleteMorphsAutomaticallyToolStripMenuItem.Checked ^= true;
		}

		#endregion Menu Strip Item Handlers
	}
}
