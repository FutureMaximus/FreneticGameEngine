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
using FGECore.EntitySystem;
using FGECore.PropertySystem;

namespace FGEGraphics.ClientSystem.EntitySystem
{
    /// <summary>Represents an entity on the client side.</summary>
    public class ClientEntity : BasicEntity<ClientEntity, GameEngineBase>
    {
        /// <summary>
        /// Get or set the renderer for this entity.
        /// Adding or removing a renderable will set this value.
        /// </summary>
        public EntityRenderableProperty Renderer = null;

        /// <summary>Gets the client window associated with this entity.</summary>
        public GameClientWindow Window => Engine.Client;
        
        /// <summary>
        /// Constructs a client-side entity.
        /// </summary>
        /// <param name="_engine">The owning game engine.</param>
        /// <param name="_ticks">Whether it should tick.</param>
        public ClientEntity(GameEngineBase _engine, bool _ticks)
            : base(_engine, _ticks)
        {
        }

        /// <summary>Called when a property is added.</summary>
        public override void OnAdded(Property prop)
        {
            base.OnAdded(prop);
            if (Renderer == null && prop is EntityRenderableProperty rnd)
            {
                Renderer = rnd;
            }
        }

        /// <summary>Called when a property is removed.</summary>
        public override void OnRemoved(Property prop)
        {
            base.OnRemoved(prop);
            if (prop == Renderer)
            {
                Renderer = null;
            }
        }
        
        /// <summary>Gets a string debug helper for this entity.</summary>
        public override string ToString()
        {
            return $"ClientEntity of type: {GetType().Name}, ID: {EID}, with properties: {PropertyList()}";
        }
    }
}
