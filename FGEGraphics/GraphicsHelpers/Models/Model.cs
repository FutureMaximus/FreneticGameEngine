﻿//
// This file is part of the Frenetic Game Engine, created by Frenetic LLC.
// This code is Copyright (C) Frenetic LLC under the terms of a strict license.
// See README.md or LICENSE.txt in the FreneticGameEngine source root for the contents of the license.
// If neither of these are available, assume that neither you nor anyone other than the copyright holder
// hold any right or permission to use this software until such time as the official license is identified.
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenTK;
using OpenTK.Graphics.OpenGL4;
using FreneticUtilities.FreneticExtensions;
using FGECore.CoreSystems;
using FGECore.MathHelpers;
using FGECore.ModelSystems;
using FGEGraphics.ClientSystem;
using FGEGraphics.ClientSystem.ViewRenderSystem;
using FGEGraphics.GraphicsHelpers.Textures;

namespace FGEGraphics.GraphicsHelpers.Models
{
    /// <summary>
    /// Represents a 3D model.
    /// </summary>
    public class Model
    {
        /// <summary>
        /// The original core model.
        /// </summary>
        public Model3D Original;

        /// <summary>
        /// Constructs the model.
        /// </summary>
        /// <param name="_name">The name.</param>
        public Model(string _name)
        {
            Name = _name;
            Meshes = new List<ModelMesh>();
            MeshMap = new Dictionary<string, ModelMesh>();
        }

        /// <summary>
        /// The root transform.
        /// </summary>
        public Matrix4 Root;

        /// <summary>
        /// The name of  this model.
        /// </summary>
        public string Name;

        /// <summary>
        /// LOD helper data.
        /// </summary>
        public KeyValuePair<int, int>[] LODHelper = null;

        /// <summary>
        /// The LOD box.
        /// </summary>
        public AABB LODBox = default;

        /// <summary>
        /// All the meshes this model has.
        /// </summary>
        public List<ModelMesh> Meshes;

        /// <summary>
        /// A map of mesh names to meshes for this model.
        /// </summary>
        public Dictionary<string, ModelMesh> MeshMap;

        /// <summary>
        /// The root node.
        /// </summary>
        public ModelNode RootNode;

        /// <summary>
        /// Whether the model bounds are set and known.
        /// </summary>
        public bool ModelBoundsSet = false;

        /// <summary>
        /// The minimum model bound.
        /// </summary>
        public BEPUutilities.Vector3 ModelMin;

        /// <summary>
        /// The maximum model bound.
        /// </summary>
        public BEPUutilities.Vector3 ModelMax;

        /// <summary>
        /// Whether the model is loaded yet.
        /// </summary>
        public bool IsLoaded = false;

        /// <summary>
        /// Adds a mesh to this model.
        /// </summary>
        /// <param name="mesh">The mesh to add.</param>
        public void AddMesh(ModelMesh mesh)
        {
            Meshes.Add(mesh);
            MeshMap[mesh.Name] = mesh;
        }

        /// <summary>
        /// Automatically builds the <see cref="MeshMap"/>.
        /// </summary>
        public void AutoMapMeshes()
        {
            MeshMap = new Dictionary<string, ModelMesh>(Meshes.Count * 2);
            foreach (ModelMesh mesh in Meshes)
            {
                MeshMap[mesh.Name] = mesh;
            }
        }

        /// <summary>
        /// Gets a mesh by name.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns>The mesh.</returns>
        public ModelMesh MeshFor(string name)
        {
            name = name.ToLowerFast();
            if (MeshMap.TryGetValue(name, out ModelMesh mesh))
            {
                return mesh;
            }
            for (int i = 0; i < Meshes.Count; i++)
            {
                // TODO: Is StartsWith needed here?
                if (Meshes[i].Name.StartsWith(name))
                {
                    return Meshes[i];
                }
            }
            return null;
        }

        /// <summary>
        /// Sets the bones to an array value.
        /// </summary>
        /// <param name="mats">The relevant array.</param>
        public void SetBones(Matrix4[] mats)
        {
            float[] set = new float[mats.Length * 16];
            for (int i = 0; i < mats.Length; i++)
            {
                for (int x = 0; x < 4; x++)
                {
                    for (int y = 0; y < 4; y++)
                    {
                        set[i * 16 + x * 4 + y] = mats[i][x, y];
                    }
                }
            }
            GL.UniformMatrix4(101, mats.Length, false, set);
        }

        /// <summary>
        /// Clears up the bones to identity.
        /// </summary>
        public void BoneSafe()
        {
            Matrix4 ident = Matrix4.Identity;
            GL.UniformMatrix4(100, false, ref ident);
            Matrix4[] mats = new Matrix4[] { ident };
            SetBones(mats);
        }

        /// <summary>
        /// Any custom animation adjustments on this model.
        /// </summary>
        public Dictionary<string, Matrix4> CustomAnimationAdjustments = new Dictionary<string, Matrix4>();

        /// <summary>
        /// Force bones not to offset.
        /// </summary>
        public bool ForceBoneNoOffset = false;

        /// <summary>
        /// Update transformations on the model.
        /// </summary>
        /// <param name="pNode">The previous node.</param>
        /// <param name="transf">The current transform.</param>
        public void UpdateTransforms(ModelNode pNode, Matrix4 transf)
        {
            string nodename = pNode.Name;
            Matrix4 nodeTransf = Matrix4.Identity;
            SingleAnimationNode pNodeAnim = FindNodeAnim(nodename, pNode.Mode, out double time);
            if (pNodeAnim != null)
            {
                BEPUutilities.Vector3 vec = pNodeAnim.LerpPos(time);
                BEPUutilities.Quaternion quat = pNodeAnim.LerpRotate(time);
                OpenTK.Quaternion oquat = new OpenTK.Quaternion((float)quat.X, (float)quat.Y, (float)quat.Z, (float)quat.W);
                Matrix4.CreateTranslation((float)vec.X, (float)vec.Y, (float)vec.Z, out Matrix4 trans);
                trans.Transpose();
                Matrix4.CreateFromQuaternion(ref oquat, out Matrix4 rot);
                if (CustomAnimationAdjustments.TryGetValue(nodename, out Matrix4 r2))
                {
                    rot *= r2;
                }
                rot.Transpose();
                Matrix4.Mult(ref trans, ref rot, out nodeTransf);
            }
            else
            {
                if (CustomAnimationAdjustments.TryGetValue(nodename, out Matrix4 temp))
                {
                    temp.Transpose();
                    nodeTransf = temp;
                }
            }
            Matrix4.Mult(ref transf, ref nodeTransf, out Matrix4 global);
            for (int i = 0; i < pNode.Bones.Count; i++)
            {
                if (ForceBoneNoOffset)
                {
                    pNode.Bones[i].Transform = global;
                }
                else
                {
                    Matrix4.Mult(ref global, ref pNode.Bones[i].Offset, out pNode.Bones[i].Transform);
                }
            }
            for (int i = 0; i < pNode.Children.Count; i++)
            {
                UpdateTransforms(pNode.Children[i], global);
            }
        }

        /// <summary>
        /// The backing model engine.
        /// </summary>
        public ModelEngine Engine = null;

        /// <summary>
        /// Finds an animation node.
        /// </summary>
        /// <param name="nodeName">The node name.</param>
        /// <param name="mode">The mode.</param>
        /// <param name="time">The time stamp result.</param>
        /// <returns>The node.</returns>
        SingleAnimationNode FindNodeAnim(string nodeName, int mode, out double time)
        {
            SingleAnimation nodes;
            if (mode == 0)
            {
                nodes = hAnim;
                time = aTHead;
            }
            else if (mode == 1)
            {
                nodes = tAnim;
                time = aTTorso;
            }
            else
            {
                nodes = lAnim;
                time = aTLegs;
            }
            if (nodes == null)
            {
                return null;
            }
            return nodes.GetNode(nodeName);
        }

        /// <summary>
        /// Head animation.
        /// </summary>
        SingleAnimation hAnim;

        /// <summary>
        /// Torso animation.
        /// </summary>
        SingleAnimation tAnim;

        /// <summary>
        /// Legs animation.
        /// </summary>
        SingleAnimation lAnim;

        /// <summary>
        /// Head animation time.
        /// </summary>
        double aTHead;

        /// <summary>
        /// Torso animation time.
        /// </summary>
        double aTTorso;

        /// <summary>
        /// Legs animation time.
        /// </summary>
        double aTLegs;

        /// <summary>
        /// The timestamp this model was last drawn at.
        /// </summary>
        public double LastDrawTime;

        /// <summary>
        /// Draws the model with low level of detail.
        /// </summary>
        /// <param name="pos">The position.</param>
        /// <param name="view">The relevant view helper.</param>
        public void DrawLOD(Location pos, View3D view)
        {
            if (LODHelper == null)
            {
                return;
            }
            Vector3 wid = (LODBox.Max - LODBox.Min).ToOpenTK();
            Vector3 vpos = (pos - view.State.RenderRelative).ToOpenTK() + new Vector3(0f, 0f, wid.Z * 0.5f);
            Vector3 offs = new Vector3(-0.5f, -0.5f, 0f);
            Matrix4 off1 = Matrix4.CreateTranslation(offs);
            //Matrix4 off2 = Matrix4.CreateTranslation(-offs);
            //Engine.TheClient.Rendering.SetMinimumLight(1f);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, LODHelper[0].Key);
            GL.ActiveTexture(TextureUnit.Texture1);
            GL.BindTexture(TextureTarget.Texture2D, LODHelper[0].Value);
            Engine.TheClient.Rendering3D.RenderRectangle3D(off1 * Matrix4.CreateScale(wid.X, wid.Z, 1f) * Matrix4.CreateRotationX((float)Math.PI * 0.5f) * Matrix4.CreateRotationZ((float)Math.PI * 0.25f) * Matrix4.CreateTranslation(vpos));
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, LODHelper[1].Key);
            GL.ActiveTexture(TextureUnit.Texture1);
            GL.BindTexture(TextureTarget.Texture2D, LODHelper[1].Value);
            Engine.TheClient.Rendering3D.RenderRectangle3D(off1 * Matrix4.CreateScale(wid.X, wid.Z, 1f) * Matrix4.CreateRotationX((float)Math.PI * 0.5f) * Matrix4.CreateRotationZ((float)Math.PI * 0.75f) * Matrix4.CreateTranslation(vpos));
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, LODHelper[2].Key);
            GL.ActiveTexture(TextureUnit.Texture1);
            GL.BindTexture(TextureTarget.Texture2D, LODHelper[2].Value);
            Engine.TheClient.Rendering3D.RenderRectangle3D(off1 * Matrix4.CreateScale(wid.Y, wid.Z, 1f) * Matrix4.CreateRotationX((float)Math.PI * 0.5f) * Matrix4.CreateRotationZ((float)Math.PI * -0.25f) * Matrix4.CreateTranslation(vpos));
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, LODHelper[3].Key);
            GL.ActiveTexture(TextureUnit.Texture1);
            GL.BindTexture(TextureTarget.Texture2D, LODHelper[3].Value);
            Engine.TheClient.Rendering3D.RenderRectangle3D(off1 * Matrix4.CreateScale(wid.Y, wid.Z, 1f) * Matrix4.CreateRotationX((float)Math.PI * 0.5f) * Matrix4.CreateRotationZ((float)Math.PI * -0.75f) * Matrix4.CreateTranslation(vpos));
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, LODHelper[4].Key);
            GL.ActiveTexture(TextureUnit.Texture1);
            GL.BindTexture(TextureTarget.Texture2D, LODHelper[4].Value);
            Engine.TheClient.Rendering3D.RenderRectangle3D(off1 * Matrix4.CreateScale(wid.Z, wid.X, 1f) * Matrix4.CreateTranslation(vpos));
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, LODHelper[5].Key);
            GL.ActiveTexture(TextureUnit.Texture1);
            GL.BindTexture(TextureTarget.Texture2D, LODHelper[5].Value);
            Engine.TheClient.Rendering3D.RenderRectangle3D(off1 * Matrix4.CreateScale(wid.Z, wid.X, 1f) * Matrix4.CreateRotationX((float)Math.PI) * Matrix4.CreateTranslation(vpos));
            GL.ActiveTexture(TextureUnit.Texture1);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            //Engine.TheClient.Rendering.SetMinimumLight(0f);
        }

        /// <summary>
        /// Draws the model.
        /// </summary>
        /// <param name="context">The sourcing render context.</param>
        /// <param name="aTimeHead">Animation time, head.</param>
        /// <param name="aTimeLegs">Animation time, legs.</param>
        /// <param name="aTimeTorso">Aniamtion time, torso.</param>
        /// <param name="forceBones">Whether to force bone setting.</param>
        /// <param name="headanim">Head animation.</param>
        /// <param name="legsanim">Legs animation.</param>
        /// <param name="torsoanim">Torso animation.</param>
        public void Draw(RenderContext context, double aTimeHead = 0, SingleAnimation headanim = null, double aTimeTorso = 0, SingleAnimation torsoanim = null, double aTimeLegs = 0, SingleAnimation legsanim = null, bool forceBones = false)
        {
            LastDrawTime = Engine.CurrentTime;
            hAnim = headanim;
            tAnim = torsoanim;
            lAnim = legsanim;
            bool any = hAnim != null || tAnim != null || lAnim != null || forceBones;
            if (any)
            {
                // globalInverse = Root.Inverted();
                aTHead = aTimeHead;
                aTTorso = aTimeTorso;
                aTLegs = aTimeLegs;
                UpdateTransforms(RootNode, Matrix4.Identity);
            }
            // TODO: If hasBones && !any { defaultBones() } ?
            for (int i = 0; i < Meshes.Count; i++)
            {
                if (any && Meshes[i].Bones.Count > 0)
                {
                    Matrix4[] mats = new Matrix4[Meshes[i].Bones.Count];
                    for (int x = 0; x < Meshes[i].Bones.Count; x++)
                    {
                        mats[x] = Meshes[i].Bones[x].Transform;
                    }
                    SetBones(mats);
                }
                Meshes[i].Draw(context);
            }
        }

        /// <summary>
        /// Whether this model has a skin already.
        /// </summary>
        public bool Skinned = false;

        /// <summary>
        /// Loads the skin for this model.
        /// </summary>
        /// <param name="texs">Texture engine.</param>
        public void LoadSkin(TextureEngine texs)
        {
            if (Skinned)
            {
                return;
            }
            Skinned = true;
            if (Engine.TheClient.Files.TryReadFileText("models/" + Name + ".skin", out string fileText))
            {
                string[] data = fileText.SplitFast('\n');
                int c = 0;
                foreach (string datum in data)
                {
                    if (datum.Length > 0)
                    {
                        string[] datums = datum.SplitFast('=');
                        if (datums.Length == 2)
                        {
                            Texture tex = texs.GetTexture(datums[1]);
                            bool success = false;
                            string datic = datums[0].BeforeAndAfter(":::", out string typer);
                            typer = typer.ToLowerFast();
                            for (int i = 0; i < Meshes.Count; i++)
                            {
                                if (Meshes[i].Name == datic)
                                {
                                    if (typer == "specular")
                                    {
                                        Meshes[i].BaseRenderable.Tex_Specular = tex;
                                    }
                                    else if (typer == "reflectivity")
                                    {
                                        Meshes[i].BaseRenderable.Tex_Reflectivity = tex;
                                    }
                                    else if (typer == "normal")
                                    {
                                        Meshes[i].BaseRenderable.Tex_Normal = tex;
                                    }
                                    else if (typer == "")
                                    {
                                        Meshes[i].BaseRenderable.Tex = tex;
                                    }
                                    else
                                    {
                                        SysConsole.Output(OutputType.WARNING, "Unknown skin entry typer: '" + typer + "', expected reflectivity, specular, or simply no specification!");
                                    }
                                    c++;
                                    success = true;
                                }
                            }
                            if (!success)
                            {
                                SysConsole.Output(OutputType.WARNING, "Unknown skin entry " + datums[0]);
                                StringBuilder all = new StringBuilder(Meshes.Count * 100);
                                for (int i = 0; i < Meshes.Count; i++)
                                {
                                    all.Append(Meshes[i].Name + ", ");
                                }
                                SysConsole.Output(OutputType.WARNING, "Available: " + all.ToString());
                            }
                        }
                    }
                }
                if (c == 0)
                {
                    SysConsole.Output(OutputType.WARNING, "No entries in " + Name + ".skin");
                }
            }
            else
            {
                SysConsole.Output(OutputType.WARNING, "Can't find models/" + Name + ".skin!");
            }
        }

        /// <summary>
        /// Gets VRAM used by this model.
        /// </summary>
        /// <returns></returns>
        public long GetVRAMUsage()
        {
            long ret = 0;
            foreach (ModelMesh mesh in Meshes)
            {
                ret += mesh.BaseRenderable.GetVRAMUsage();
            }
            return ret;
        }
    }
}
