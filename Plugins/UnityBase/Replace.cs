﻿using System;
using System.Collections.Generic;
using System.IO;
using SlimDX;

using SB3Utility;

namespace UnityPlugin
{
	public static partial class Operations
	{
		public static Transform CreateTransformTree(Animator parser, ImportedFrame frame, Transform parent)
		{
			Transform trans = new Transform(parser.file);
			GameObject gameObj = new GameObject(parser.file);
			gameObj.m_Name = (string)frame.Name.Clone();
			UniqueName(parser, gameObj);
			gameObj.AddLinkedComponent(trans);

			parser.m_Avatar.instance.AddBone(parent, trans);

			Vector3 t, s;
			Quaternion r;
			frame.Matrix.Decompose(out s, out r, out t);
			t.X *= -1;
			Vector3 euler = FbxUtility.QuaternionToEuler(r);
			euler.Y *= -1;
			euler.Z *= -1;
			trans.m_LocalRotation = FbxUtility.EulerToQuaternion(euler);
			trans.m_LocalPosition = t;
			trans.m_LocalScale = s;

			trans.InitChildren(frame.Count);
			for (int i = 0; i < frame.Count; i++)
			{
				trans.AddChild(CreateTransformTree(parser, frame[i], trans));
			}

			return trans;
		}

		public static Transform CloneTransformTree(Animator parser, Transform frame, Transform parent)
		{
			Transform trans = new Transform(parser.file);
			GameObject gameObj = new GameObject(parser.file);
			gameObj.m_Name = (string)frame.m_GameObject.instance.m_Name.Clone();
			UniqueName(parser, gameObj);
			gameObj.AddLinkedComponent(trans);

			parser.m_Avatar.instance.AddBone(parent, trans);

			trans.m_LocalRotation = frame.m_LocalRotation;
			trans.m_LocalPosition = frame.m_LocalPosition;
			trans.m_LocalScale = frame.m_LocalScale;
			CopyUnknowns(frame, trans);

			trans.InitChildren(frame.Count);
			for (int i = 0; i < frame.Count; i++)
			{
				trans.AddChild(CloneTransformTree(parser, frame[i], trans));
			}

			return trans;
		}

		static void UniqueName(Animator parser, GameObject gameObj)
		{
			string name = gameObj.m_Name;
			int attempt = 1;
			while (FindFrame(gameObj.m_Name, parser.RootTransform) != null)
			{
				gameObj.m_Name = name + ++attempt;
			}
		}

		public static void UniqueName(Animator parser, Transform frame, List<string> frameNames)
		{
			GameObject gameObj = frame.m_GameObject.instance;
			string frameName = gameObj.m_Name;
			int attempt = 1;
			while (FindFrame(gameObj.m_Name, parser.RootTransform) != null)
			{
				gameObj.m_Name = frameName + ++attempt;
			}
			/*if (rename && attempt > 1)
			{
				parser.m_Avatar.instance.RenameBone(frameName, gameObj.m_Name);
			}

			Transform parent = frame.Parent;
			string parentPath = Parser.m_Avatar.instance.BonePath(parent.m_GameObject.instance.m_Name);
			string framePath = Parser.m_Avatar.instance.BonePath(frame.m_GameObject.instance.m_Name);
			int childIndex = parent.IndexOf(frame);
			parent.RemoveChild(frame);
			Operations.UniqueName(Parser, frame.m_GameObject.instance, true);
			parent.InsertChild(childIndex, frame);

			for (int i = 0; i < frame.Count; i++)
			{
				UniqueFrame(frame[i]);
			}*/

		}

		public static void CopyOrCreateUnknowns(Transform dest, Transform root)
		{
			Transform src = FindFrame(dest.m_GameObject.instance.m_Name, root);
			if (src == null)
			{
				CreateUnknowns(dest);
			}
			else
			{
				CopyUnknowns(src, dest);
			}

			for (int i = 0; i < dest.Count; i++)
			{
				CopyOrCreateUnknowns(dest[i], root);
			}
		}

		public static List<PPtr<Transform>> CreateBoneList(Transform root, List<ImportedBone> boneList, List<Matrix> poseMatrices)
		{
			List<PPtr<Transform>> uBoneList = new List<PPtr<Transform>>(boneList.Count);
			string message = string.Empty;
			for (int i = 0; i < boneList.Count; i++)
			{
				Transform boneFrame = FindFrame(boneList[i].Name, root);
				if (boneFrame == null)
				{
					message += " " + boneList[i].Name;
				}

				Vector3 s, t;
				Quaternion q;
				boneList[i].Matrix.Decompose(out s, out q, out t);
				t.X *= -1;
				Vector3 euler = FbxUtility.QuaternionToEuler(q);
				euler.Y *= -1;
				euler.Z *= -1;
				q = FbxUtility.EulerToQuaternion(euler);
				poseMatrices.Add(Matrix.Transpose(Matrix.Scaling(s) * Matrix.RotationQuaternion(q) * Matrix.Translation(t)));

				uBoneList.Add(new PPtr<Transform>(boneFrame));
			}
			if (message != string.Empty)
			{
				throw new Exception("Boneframe(s) not found:" + message);
			}
			return uBoneList;
		}

		public static SkinnedMeshRenderer CreateSkinnedMeshRenderer(Animator parser, List<Material> materials, WorkspaceMesh mesh, out int[] indices, out bool[] worldCoords, out bool[] replaceSubmeshesOption)
		{
			int numUncheckedSubmeshes = 0;
			foreach (ImportedSubmesh submesh in mesh.SubmeshList)
			{
				if (!mesh.isSubmeshEnabled(submesh))
				{
					numUncheckedSubmeshes++;
				}
			}
			int numSubmeshes = mesh.SubmeshList.Count - numUncheckedSubmeshes;
			indices = new int[numSubmeshes];
			worldCoords = new bool[numSubmeshes];
			replaceSubmeshesOption = new bool[numSubmeshes];

			List<Matrix> poseMatrices = new List<Matrix>(mesh.BoneList.Count);
			List<PPtr<Transform>> bones = CreateBoneList(parser.m_GameObject.instance.FindLinkedComponent(UnityClassID.Transform), mesh.BoneList, poseMatrices);

			SkinnedMeshRenderer sMesh = new SkinnedMeshRenderer(parser.file);

			int totVerts = 0, totFaces = 0;
			sMesh.m_Materials.Capacity = numSubmeshes;
			foreach (ImportedSubmesh submesh in mesh.SubmeshList)
			{
				if (!mesh.isSubmeshEnabled(submesh))
				{
					continue;
				}

				Material matFound = materials.Find
				(
					delegate(Material mat)
					{
						return mat.m_Name == submesh.Material;
					}
				);
				sMesh.m_Materials.Add(new PPtr<Material>(matFound));

				totVerts += submesh.VertexList.Count;
				totFaces += submesh.FaceList.Count;
			}
			Mesh uMesh = new Mesh(parser.file);
			uMesh.m_Name = mesh.Name;
			sMesh.m_Mesh = new PPtr<Mesh>(uMesh);

			sMesh.m_Bones = bones;
			uMesh.m_BindPose = poseMatrices;
			uMesh.m_BoneNameHashes = new List<uint>(poseMatrices.Count);
			for (int i = 0; i < mesh.BoneList.Count; i++)
			{
				string bone = mesh.BoneList[i].Name;
				uint hash = parser.m_Avatar.instance.BoneHash(bone);
				uMesh.m_BoneNameHashes.Add(hash);
			}

			uMesh.m_VertexData = new VertexData((uint)totVerts);
			uMesh.m_Skin = new List<BoneInfluence>(totVerts);
			uMesh.m_IndexBuffer = new byte[totFaces * 3 * sizeof(ushort)];
			using (BinaryWriter vertWriter = new BinaryWriter(new MemoryStream(uMesh.m_VertexData.m_DataSize)),
					indexWriter = new BinaryWriter(new MemoryStream(uMesh.m_IndexBuffer)))
			{
				uMesh.m_LocalAABB.m_Center = new Vector3(Single.MaxValue, Single.MaxValue, Single.MaxValue);
				uMesh.m_LocalAABB.m_Extend = new Vector3(Single.MinValue, Single.MinValue, Single.MinValue);
				uMesh.m_SubMeshes = new List<SubMesh>(numSubmeshes);
				int vertIndex = 0;
				for (int i = 0, submeshIdx = 0; i < numSubmeshes; i++, submeshIdx++)
				{
					while (!mesh.isSubmeshEnabled(mesh.SubmeshList[submeshIdx]))
					{
						submeshIdx++;
					}

					SubMesh submesh = new SubMesh();
					submesh.indexCount = (uint)mesh.SubmeshList[submeshIdx].FaceList.Count * 3;
					submesh.vertexCount = (uint)mesh.SubmeshList[submeshIdx].VertexList.Count;
					submesh.firstVertex = (uint)vertIndex;
					uMesh.m_SubMeshes.Add(submesh);

					indices[i] = mesh.SubmeshList[submeshIdx].Index;
					worldCoords[i] = mesh.SubmeshList[submeshIdx].WorldCoords;
					replaceSubmeshesOption[i] = mesh.isSubmeshReplacingOriginal(mesh.SubmeshList[submeshIdx]);

					List<ImportedVertex> vertexList = mesh.SubmeshList[submeshIdx].VertexList;
					Vector3 min = new Vector3(Single.MaxValue, Single.MaxValue, Single.MaxValue);
					Vector3 max = new Vector3(Single.MinValue, Single.MinValue, Single.MinValue);
					for (int str = 0; str < uMesh.m_VertexData.m_Streams.Count; str++)
					{
						StreamInfo sInfo = uMesh.m_VertexData.m_Streams[str];
						if (sInfo.channelMask == 0)
						{
							continue;
						}

						for (int j = 0; j < vertexList.Count; j++)
						{
							ImportedVertex vert = vertexList[j];
							for (int chn = 0; chn < uMesh.m_VertexData.m_Channels.Count; chn++)
							{
								ChannelInfo cInfo = uMesh.m_VertexData.m_Channels[chn];
								if ((sInfo.channelMask & (1 << chn)) == 0)
								{
									continue;
								}

								vertWriter.BaseStream.Position = sInfo.offset + (j + submesh.firstVertex) * sInfo.stride + cInfo.offset;
								switch (chn)
								{
								case 0:
									Vector3 pos = vert.Position;
									pos.X *= -1;
									vertWriter.Write(pos);
									min = Vector3.Minimize(min, pos);
									max = Vector3.Maximize(max, pos);
									break;
								case 1:
									Vector3 normal = vert.Normal;
									normal.X *= -1;
									vertWriter.Write(normal);
									break;
								case 3:
									vertWriter.Write(vert.UV);
									break;
								case 5:
									Vector4 tangent = vert.Tangent;
									tangent.X *= -1;
									tangent.W *= -1;
									vertWriter.Write(vert.Tangent);
									break;
								}
							}

							if (sMesh.m_Bones.Count > 0 && sInfo.offset == 0 && uMesh.m_Skin.Count < totVerts)
							{
								BoneInfluence item = new BoneInfluence();
								for (int k = 0; k < 4; k++)
								{
									item.boneIndex[k] = vert.BoneIndices[k] != 0xFF ? vert.BoneIndices[k] : 0;
								}
								vert.Weights.CopyTo(item.weight, 0);
								uMesh.m_Skin.Add(item);
							}
						}
					}
					vertIndex += (int)submesh.vertexCount;

					submesh.localAABB.m_Extend = max - min;
					submesh.localAABB.m_Center = min + submesh.localAABB.m_Extend / 2;
					uMesh.m_LocalAABB.m_Extend = Vector3.Maximize(uMesh.m_LocalAABB.m_Extend, max);
					uMesh.m_LocalAABB.m_Center = Vector3.Minimize(uMesh.m_LocalAABB.m_Center, min);

					List<ImportedFace> faceList = mesh.SubmeshList[submeshIdx].FaceList;
					submesh.firstByte = (uint)indexWriter.BaseStream.Position;
					for (int j = 0; j < faceList.Count; j++)
					{
						int[] vertexIndices = faceList[j].VertexIndices;
						indexWriter.Write((ushort)(vertexIndices[0] + submesh.firstVertex));
						indexWriter.Write((ushort)(vertexIndices[2] + submesh.firstVertex));
						indexWriter.Write((ushort)(vertexIndices[1] + submesh.firstVertex));
					}
				}
				uMesh.m_LocalAABB.m_Extend -= uMesh.m_LocalAABB.m_Center;
				uMesh.m_LocalAABB.m_Center += uMesh.m_LocalAABB.m_Extend / 2;
			}

			return sMesh;
		}

		public static void ReplaceMeshRenderer(Transform frame, Transform rootBone, Animator parser, List<Material> materials, WorkspaceMesh mesh, bool merge, CopyMeshMethod normalsMethod, CopyMeshMethod bonesMethod, bool targetFullMesh)
		{
			Matrix transform = Transform.WorldTransform(frame);
			transform.Invert();

			int[] indices;
			bool[] worldCoords;
			bool[] replaceSubmeshesOption;
			SkinnedMeshRenderer sMesh = CreateSkinnedMeshRenderer(parser, materials, mesh, out indices, out worldCoords, out replaceSubmeshesOption);
			vMesh destMesh = new Operations.vMesh(sMesh, true, false);

			SkinnedMeshRenderer sFrameMesh = frame.m_GameObject.instance.FindLinkedComponent(UnityClassID.SkinnedMeshRenderer);
			MeshRenderer frameMeshR = sFrameMesh;
			if (sFrameMesh == null)
			{
				frameMeshR = frame.m_GameObject.instance.FindLinkedComponent(UnityClassID.MeshRenderer);
			}
			Mesh frameMesh = frameMeshR != null ? Operations.GetMesh(frameMeshR) : null;
			vMesh srcMesh = null;
			List<vVertex> allVertices = null;
			if (frameMeshR == null || frameMesh == null)
			{
				sMesh.m_RootBone = new PPtr<Transform>(rootBone);
				if (rootBone != null)
				{
					sMesh.m_Mesh.instance.m_RootBoneNameHash = parser.m_Avatar.instance.BoneHash(rootBone.m_GameObject.instance.m_Name);
				}
				if (frameMeshR != null)
				{
					CopyUnknowns(frameMeshR, sMesh);
				}
			}
			else
			{
				if (sFrameMesh != null)
				{
					sMesh.m_RootBone = new PPtr<Transform>(sFrameMesh.m_RootBone.instance);
					sMesh.m_Mesh.instance.m_RootBoneNameHash = frameMesh.m_RootBoneNameHash;
				}
				else
				{
					sMesh.m_RootBone = new PPtr<Transform>((Component)null);
				}

				srcMesh = new Operations.vMesh(frameMeshR, true, false);
				CopyUnknowns(frameMeshR, sMesh);

				if (targetFullMesh && (normalsMethod == CopyMeshMethod.CopyNear || bonesMethod == CopyMeshMethod.CopyNear))
				{
					allVertices = new List<vVertex>();
					HashSet<Vector3> posSet = new HashSet<Vector3>();
					foreach (vSubmesh submesh in srcMesh.submeshes)
					{
						allVertices.Capacity = allVertices.Count + submesh.vertexList.Count;
						foreach (vVertex vertex in submesh.vertexList)
						{
							if (!posSet.Contains(vertex.position))
							{
								posSet.Add(vertex.position);
								allVertices.Add(vertex);
							}
						}
					}
				}
			}

			vSubmesh[] replaceSubmeshes = (srcMesh == null) ? null : new vSubmesh[srcMesh.submeshes.Count];
			List<vSubmesh> addSubmeshes = new List<vSubmesh>(destMesh.submeshes.Count);
			for (int i = 0; i < destMesh.submeshes.Count; i++)
			{
				vSubmesh submesh = destMesh.submeshes[i];
				List<vVertex> vVertexList = submesh.vertexList;
				if (worldCoords[i])
				{
					for (int j = 0; j < vVertexList.Count; j++)
					{
						vVertexList[j].position = Vector3.TransformCoordinate(vVertexList[j].position, transform);
					}
				}

				vSubmesh baseSubmesh = null;
				int idx = indices[i];
				if ((srcMesh != null) && (idx >= 0) && (idx < frameMesh.m_SubMeshes.Count))
				{
					baseSubmesh = srcMesh.submeshes[idx];
					CopyUnknowns(frameMesh.m_SubMeshes[idx], sMesh.m_Mesh.instance.m_SubMeshes[i]);
				}

				if (baseSubmesh != null)
				{
					if (normalsMethod == CopyMeshMethod.CopyOrder)
					{
						Operations.CopyNormalsOrder(baseSubmesh.vertexList, submesh.vertexList);
					}
					else if (normalsMethod == CopyMeshMethod.CopyNear)
					{
						Operations.CopyNormalsNear(targetFullMesh ? allVertices : baseSubmesh.vertexList, submesh.vertexList);
					}

					if (baseSubmesh.vertexList[0].weights != null)
					{
						if (bonesMethod == CopyMeshMethod.CopyOrder)
						{
							Operations.CopyBonesOrder(baseSubmesh.vertexList, submesh.vertexList);
						}
						else if (bonesMethod == CopyMeshMethod.CopyNear)
						{
							Operations.CopyBonesNear(targetFullMesh ? allVertices : baseSubmesh.vertexList, submesh.vertexList);
						}
					}
				}

				if ((baseSubmesh != null) && merge && replaceSubmeshesOption[i])
				{
					replaceSubmeshes[idx] = submesh;
				}
				else
				{
					addSubmeshes.Add(submesh);
				}
			}

			if ((srcMesh != null) && merge)
			{
				destMesh.submeshes = new List<vSubmesh>(replaceSubmeshes.Length + addSubmeshes.Count);
				List<vSubmesh> copiedSubmeshes = new List<vSubmesh>(replaceSubmeshes.Length);
				for (int i = 0; i < replaceSubmeshes.Length; i++)
				{
					if (replaceSubmeshes[i] == null)
					{
						vSubmesh srcSubmesh = srcMesh.submeshes[i];
						copiedSubmeshes.Add(srcSubmesh);
						destMesh.submeshes.Add(srcSubmesh);
					}
					else
					{
						destMesh.submeshes.Add(replaceSubmeshes[i]);
					}
				}
				destMesh.submeshes.AddRange(addSubmeshes);

				if ((sFrameMesh == null || sFrameMesh.m_Bones.Count == 0) && (sMesh.m_Bones.Count > 0))
				{
					for (int i = 0; i < copiedSubmeshes.Count; i++)
					{
						List<vVertex> vertexList = copiedSubmeshes[i].vertexList;
						for (int j = 0; j < vertexList.Count; j++)
						{
							vertexList[j].boneIndices = new int[4] { 0, 0, 0, 0 };
							vertexList[j].weights = new float[4] { 0, 0, 0, 0 };
						}
					}
				}
				else if (sFrameMesh != null && sFrameMesh.m_Bones.Count > 0)
				{
					int[] boneIdxMap;
					sMesh.m_Bones = MergeBoneList(sFrameMesh.m_Bones, sMesh.m_Bones, out boneIdxMap);
					uint[] boneHashes = new uint[sMesh.m_Bones.Count];
					Matrix[] poseMatrices = new Matrix[sMesh.m_Bones.Count];
					for (int i = 0; i < sFrameMesh.m_Bones.Count; i++)
					{
						boneHashes[i] = sFrameMesh.m_Mesh.instance.m_BoneNameHashes[i];
						poseMatrices[i] = sFrameMesh.m_Mesh.instance.m_BindPose[i];
					}
					for (int i = 0; i < boneIdxMap.Length; i++)
					{
						boneHashes[boneIdxMap[i]] = sMesh.m_Mesh.instance.m_BoneNameHashes[i];
						poseMatrices[boneIdxMap[i]] = sMesh.m_Mesh.instance.m_BindPose[i];
					}
					sMesh.m_Mesh.instance.m_BoneNameHashes.Clear();
					sMesh.m_Mesh.instance.m_BoneNameHashes.AddRange(boneHashes);
					sMesh.m_Mesh.instance.m_BindPose.Clear();
					sMesh.m_Mesh.instance.m_BindPose.AddRange(poseMatrices);

					if (bonesMethod == CopyMeshMethod.Replace)
					{
						for (int i = 0; i < replaceSubmeshes.Length; i++)
						{
							if (replaceSubmeshes[i] != null)
							{
								List<vVertex> vertexList = replaceSubmeshes[i].vertexList;
								if (vertexList[0].boneIndices != null)
								{
									for (int j = 0; j < vertexList.Count; j++)
									{
										int[] boneIndices = vertexList[j].boneIndices;
										vertexList[j].boneIndices = new int[4];
										for (int k = 0; k < 4; k++)
										{
											vertexList[j].boneIndices[k] = boneIdxMap[boneIndices[k]];
										}
									}
								}
							}
						}
						for (int i = 0; i < addSubmeshes.Count; i++)
						{
							List<vVertex> vertexList = addSubmeshes[i].vertexList;
							if (vertexList[0].boneIndices != null)
							{
								for (int j = 0; j < vertexList.Count; j++)
								{
									int[] boneIndices = vertexList[j].boneIndices;
									vertexList[j].boneIndices = new int[4];
									for (int k = 0; k < 4; k++)
									{
										vertexList[j].boneIndices[k] = boneIdxMap[boneIndices[k]];
									}
								}
							}
						}
					}
				}
			}
			destMesh.Flush();

			if (frameMeshR != null)
			{
				frame.m_GameObject.instance.RemoveLinkedComponent(frameMeshR);
				//parser.file.RemoveSubfile(frameMeshR);
				if (frameMesh != null)
				{
					//parser.file.RemoveSubfile(frameMesh);
					parser.file.ReplaceSubfile(frameMesh, sMesh.m_Mesh.asset);
				}
				parser.file.ReplaceSubfile(frameMeshR, sMesh);
			}
			frame.m_GameObject.instance.AddLinkedComponent(sMesh);

			AssetBundle bundle = parser.file.Bundle;
			if (bundle != null)
			{
				if (frameMeshR != null)
				{
					if (frameMesh != null)
					{
						bundle.ReplaceComponent(frameMesh, sMesh.m_Mesh.asset);
					}
					bundle.ReplaceComponent(frameMeshR, sMesh);
				}
				else
				{
					bundle.RegisterForUpdate(parser.m_GameObject.asset);
				}
			}
		}

		public static void ReplaceMaterial(UnityParser parser, ImportedMaterial material)
		{
			for (int i = 0; i < parser.Cabinet.Components.Count; i++)
			{
				Component comp = parser.Cabinet.Components[i];
				if (comp.classID1 == UnityClassID.Material)
				{
					Material mat = parser.Cabinet.LoadComponent(comp.pathID);
					if (mat.m_Name == material.Name)
					{
						ReplaceMaterial(mat, material);
						return;
					}
				}
			}

			throw new Exception("Replacing a material currently requires an existing material with the same name");
		}

		public static void ReplaceTexture(UnityParser parser, ImportedTexture texture)
		{
			Texture2D tex = parser.GetTexture(Path.GetFileNameWithoutExtension(texture.Name));
			if (tex == null)
			{
				parser.AddTexture(texture);
				return;
			}
			ReplaceTexture(tex, texture);
		}

		public static void ReplaceMaterial(Material mat, ImportedMaterial material)
		{
			if (mat == null)
			{
				throw new Exception("Replacing a material currently requires an existing material with the same name");
			}

			for (int i = 0; i < mat.m_SavedProperties.m_Colors.Count; i++)
			{
				var col = mat.m_SavedProperties.m_Colors[i];
				Color4 att;
				switch (col.Key.name)
				{
				case "_Color":
					att = material.Diffuse;
					break;
				case "_SColor":
					att = material.Ambient;
					break;
				case "_ReflectColor":
					att = material.Emissive;
					break;
				case "_SpecColor":
					att = material.Specular;
					break;
				case "_RimColor":
				case "_OutlineColor":
				case "_ShadowColor":
				default:
					continue;
				}
				mat.m_SavedProperties.m_Colors.RemoveAt(i);
				col = new KeyValuePair<FastPropertyName, Color4>(col.Key, att);
				mat.m_SavedProperties.m_Colors.Insert(i, col);
			}

			for (int i = 0; i < mat.m_SavedProperties.m_Floats.Count; i++)
			{
				var flt = mat.m_SavedProperties.m_Floats[i];
				float att;
				switch (flt.Key.name)
				{
				case "_Shininess":
					att = material.Power;
					break;
				case "_RimPower":
				case "_Outline":
				default:
					continue;
				}
				mat.m_SavedProperties.m_Floats.RemoveAt(i);
				flt = new KeyValuePair<FastPropertyName, float>(flt.Key, att);
				mat.m_SavedProperties.m_Floats.Insert(i, flt);
			}

			for (int i = 0; i < material.Textures.Length && i < mat.m_SavedProperties.m_TexEnvs.Count; i++)
			{
				try
				{
					Texture2D tex = null;
					if (material.Textures[i] != string.Empty)
					{
						tex = mat.file.Parser.GetTexture(material.Textures[i]);
					}
					if (mat.m_SavedProperties.m_TexEnvs[i].Value.m_Texture.asset != tex)
					{
						mat.m_SavedProperties.m_TexEnvs[i].Value.m_Texture = new PPtr<Texture2D>(tex);
					}
				}
				catch (Exception e)
				{
					Report.ReportLog(e.ToString());
				}
			}
		}

		public static void ReplaceTexture(Texture2D tex, ImportedTexture texture)
		{
			if (tex == null)
			{
				throw new Exception("This type of replacing a texture requires an existing texture with the same name");
			}

			Texture2D t2d = new Texture2D(null, tex.pathID, tex.classID1, tex.classID2);
			t2d.LoadFrom(texture);
			tex.m_MipMap = t2d.m_MipMap;
			tex.m_Width = t2d.m_Width;
			tex.m_Height = t2d.m_Height;
			tex.m_CompleteImageSize = t2d.m_CompleteImageSize;
			tex.m_TextureFormat = t2d.m_TextureFormat;
			tex.m_ImageCount = t2d.m_ImageCount;
			tex.m_TextureDimension = t2d.m_TextureDimension;
			tex.image_data = t2d.image_data;
		}
	}
}
