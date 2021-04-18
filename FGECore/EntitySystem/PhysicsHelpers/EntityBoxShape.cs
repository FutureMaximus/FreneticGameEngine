//
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
using BepuPhysics.Collidables;
using FGECore.MathHelpers;
using FGECore.PhysicsSystem;
using FGECore.PropertySystem;

namespace FGECore.EntitySystem.PhysicsHelpers
{
    /// <summary>A box shape for an entity.</summary>
    public class EntityBoxShape : EntityShapeHelper
    {
        /// <summary>Constructs a new <see cref="EntityBoxShape"/> of the specified size.</summary>
        public EntityBoxShape(Location size, PhysicsSpace space)
        {
            Box box = new Box(size.XF, size.YF, size.ZF);
            BepuShape = box;
            ShapeIndex = space.Internal.CoreSimulation.Shapes.Add(box);
        }

        /// <summary>Implements <see cref="Object.ToString"/>.</summary>
        public override string ToString()
        {
            Box box = (Box)BepuShape;
            return $"EntityBoxShape({box.Width}, {box.Height}, {box.Length})";
        }
    }
}
