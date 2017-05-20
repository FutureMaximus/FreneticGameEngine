﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenTK;
using FreneticGameCore;

namespace FreneticGameGraphics
{
    /// <summary>
    /// Helpers for various external classes.
    /// </summary>
    public static class SystemExtensions
    {
        /// <summary>
        /// Converts a location to an OpenTK Vector3D.
        /// </summary>
        /// <param name="l">The location.</param>
        /// <returns>The OpenTK Vector3D.</returns>
        public static Vector3d ToOpenTK3D(this Location l)
        {
            return new Vector3d(l.X, l.Y, l.Z);
        }

        /// <summary>
        /// Converts a location to an OpenTK Vector3.
        /// </summary>
        /// <param name="l">The location.</param>
        /// <returns>The OpenTK Vector3.</returns>
        public static Vector3 ToOpenTK(this Location l)
        {
            return new Vector3((float)l.X, (float)l.Y, (float)l.Z);
        }

        /// <summary>
        /// Converts an OpenTK Vector3D to a location.
        /// </summary>
        /// <param name="l">The OpenTK Vector3D.</param>
        /// <returns>The location.</returns>
        public static Location ToLocation(this Vector3d l)
        {
            return new Location(l.X, l.Y, l.Z);
        }

        /// <summary>
        /// Converts an OpenTK Vector3 to a location.
        /// </summary>
        /// <param name="l">The OpenTK Vector3.</param>
        /// <returns>The location.</returns>
        public static Location ToLocation(this Vector3 l)
        {
            return new Location(l.X, l.Y, l.Z);
        }

        /// <summary>
        /// Converts a BEPU Matrix4 to an OpenTK Matrix4 (Doubles).
        /// </summary>
        /// <param name="mat">The input matrix.</param>
        /// <returns>The output matrix.</returns>
        public static Matrix4d ConvertD(this BEPUutilities.Matrix mat)
        {
            return new Matrix4d(mat.M11, mat.M12, mat.M13, mat.M14, mat.M21, mat.M22, mat.M23,
                mat.M24, mat.M31, mat.M32, mat.M33, mat.M34, mat.M41, mat.M42, mat.M43, mat.M44);
        }

        /// <summary>
        /// Converts an OpenTK Matrix4 to a BEPU Matrix4 (Doubles).
        /// </summary>
        /// <param name="mat">The input matrix.</param>
        /// <returns>The output matrix.</returns>
        public static BEPUutilities.Matrix ConvertD(this Matrix4d mat)
        {
            return new BEPUutilities.Matrix(mat.M11, mat.M12, mat.M13, mat.M14, mat.M21, mat.M22, mat.M23,
                mat.M24, mat.M31, mat.M32, mat.M33, mat.M34, mat.M41, mat.M42, mat.M43, mat.M44);
        }
    }
}