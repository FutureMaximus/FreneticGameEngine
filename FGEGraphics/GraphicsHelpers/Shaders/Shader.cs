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
using System.IO;
using System.Threading.Tasks;
using FGECore;
using FGECore.CoreSystems;
using FGECore.MathHelpers;
using FGECore.FileSystems;
using OpenTK.Graphics.OpenGL4;
using FGECore.ConsoleHelpers;
using FreneticUtilities.FreneticExtensions;

namespace FGEGraphics.GraphicsHelpers.Shaders
{
    /// <summary>
    /// Wraps an OpenGL shader.
    /// </summary>
    public class Shader
    {
        /// <summary>
        /// Constructs an empty shader.
        /// </summary>
        public Shader()
        {
            NewVersion = this;
        }

        /// <summary>
        /// The shader engine that owns this shader.
        /// </summary>
        public ShaderEngine Engine;

        /// <summary>
        /// The name of the shader
        /// </summary>
        public string Name;

        /// <summary>
        /// The shader this shader was remapped to.
        /// </summary>
        public Shader RemappedTo;

        /// <summary>
        /// The internal OpenGL ID for the shader program.
        /// </summary>
        public int Internal_Program;

        /// <summary>
        /// The original OpenGL ID that formed this shader program.
        /// </summary>
        public int Original_Program;

        /// <summary>
        /// All variables on this shader.
        /// </summary>
        public string[] Vars;

        /// <summary>
        /// Whether the shader loaded properly.
        /// </summary>
        public bool LoadedProperly = false;

        /// <summary>
        /// Destroys the OpenGL program that this shader wraps.
        /// </summary>
        public void Destroy()
        {
            if (Original_Program > -1 && GL.IsProgram(Original_Program))
            {
                GL.DeleteProgram(Original_Program);
                Original_Program = -1;
            }
        }

        /// <summary>
        /// Removes the shader from the system.
        /// </summary>
        public void Remove()
        {
            Destroy();
            if (Engine.LoadedShaders.TryGetValue(Name, out Shader shad) && shad == this)
            {
                Engine.LoadedShaders.Remove(Name);
            }
        }

        /// <summary>
        /// The tick time this shader was last bound.
        /// </summary>
        public double LastBindTime = 0;

        /// <summary>
        /// A new version of the shader, that replaces this one.
        /// </summary>
        private Shader NewVersion = null;

        /// <summary>
        /// Checks if the shader is valid, and replaces it if needed.
        /// </summary>
        public void CheckValid()
        {
            if (Internal_Program == -1)
            {
                Shader temp = Engine.GetShader(Name);
                Original_Program = temp.Original_Program;
                Internal_Program = Original_Program;
                RemappedTo = temp;
                NewVersion = temp;
            }
            else if (RemappedTo != null)
            {
                RemappedTo.CheckValid();
                Internal_Program = RemappedTo.Original_Program;
            }
        }

        /// <summary>
        /// Binds this shader to OpenGL.
        /// </summary>
        public Shader Bind()
        {
            if (NewVersion != this)
            {
                return NewVersion.Bind();
            }
            LastBindTime = Engine.cTime;
            CheckValid();
            GL.UseProgram(Internal_Program);
            return NewVersion;
        }
    }
}
