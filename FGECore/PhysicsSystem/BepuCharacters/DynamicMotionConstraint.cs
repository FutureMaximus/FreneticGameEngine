//
// This file is part of the Frenetic Game Engine, created by Frenetic LLC.
// This code is Copyright (C) Frenetic LLC under the terms of a strict license.
// See README.md or LICENSE.txt in the FreneticGameEngine source root for the contents of the license.
// If neither of these are available, assume that neither you nor anyone other than the copyright holder
// hold any right or permission to use this software until such time as the official license is identified.
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuUtilities;
using BepuUtilities.Memory;

using static BepuUtilities.GatherScatter;

namespace FGECore.PhysicsSystem.BepuCharacters
{
    // The majority of code in this file came from the BEPUPhysicsv2 demos.

    /// <summary>Not documented in BEPU source.</summary>
    public struct CharacterMotionAccumulatedImpulse
    {
        /// <summary>Not documented in BEPU source.</summary>
        public Vector2Wide Horizontal;
        /// <summary>Not documented in BEPU source.</summary>
        public Vector<float> Vertical;
    }

    //Constraint descriptions provide an explicit mapping from the array-of-structures format to the internal array-of-structures-of-arrays format used by the solver.
    //Note that there is a separate description for the one and two body case- constraint implementations take advantage of the lack of a second body to reduce data gathering requirements.
    /// <summary>Description of a character motion constraint where the support is dynamic.</summary>
    public struct DynamicCharacterMotionConstraint : ITwoBodyConstraintDescription<DynamicCharacterMotionConstraint>
    {
        /// <summary>Maximum force that the horizontal motion constraint can apply to reach the current velocity goal.</summary>
        public float MaximumHorizontalForce;
        /// <summary>Maximum force that the vertical motion constraint can apply to fight separation.</summary>
        public float MaximumVerticalForce;
        /// <summary>Target horizontal velocity in terms of the basis X and -Z axes.</summary>
        public Vector2 TargetVelocity;
        /// <summary>Depth of the supporting contact. The vertical motion constraint permits separating velocity if, after a frame, the objects will still be touching.</summary>
        public float Depth;
        /// <summary>
        /// Stores the quaternion-packed orthonormal basis for the motion constraint. When expanded into a matrix, X and Z will represent the Right and Backward directions respectively. Y will represent Up.
        /// In other words, a target tangential velocity of (4, 2) will result in a goal velocity of 4 along the (1, 0, 0) * Basis direction and a goal velocity of 2 along the (0, 0, -1) * Basis direction.
        /// All motion moving along the (0, 1, 0) * Basis axis will be fought against by the vertical motion constraint.
        /// </summary>
        public Quaternion SurfaceBasis;
        /// <summary>World space offset from the character's center to apply impulses at.</summary>
        public Vector3 OffsetFromCharacterToSupportPoint;
        /// <summary>World space offset from the support's center to apply impulses at.</summary>
        public Vector3 OffsetFromSupportToSupportPoint;

        //It's possible to create multiple descriptions for the same underlying constraint type id which can update different parts of the constraint data.
        //This functionality isn't used very often, though- you'll notice that the engine has a 1:1 mapping (at least at the time of this writing).
        //But in principle, it doesn't have to be that way. So, the description must provide information about the type and type id.
        /// <summary>Gets the constraint type id that this description is associated with.</summary>
        public int ConstraintTypeId => DynamicCharacterMotionTypeProcessor.BatchTypeId;

        /// <summary>Gets the TypeProcessor type that is associated with this description.</summary>
        public Type TypeProcessorType => typeof(DynamicCharacterMotionTypeProcessor);

        //Note that these mapping functions use a "GetOffsetInstance" function. Each CharacterMotionPrestep is a bundle of multiple constraints;
        //by grabbing an offset instance, we're selecting a specific slot in the bundle to modify. For simplicity and to guarantee consistency of field strides,
        //we refer to that slot using the same struct and then write only to the first slot.
        //(Note that accessing slots after the first may result in access violations; the 'offset instance' is not guaranteed to refer to valid data beyond the first slot!)
        /// <summary>Not documented in BEPU source.</summary>
        public void ApplyDescription(ref TypeBatch batch, int bundleIndex, int innerIndex)
        {
            ref var target = ref GetOffsetInstance(ref Buffer<DynamicCharacterMotionPrestep>.Get(ref batch.PrestepData, bundleIndex), innerIndex);
            QuaternionWide.WriteFirst(SurfaceBasis, ref target.SurfaceBasis);
            GetFirst(ref target.MaximumHorizontalForce) = MaximumHorizontalForce;
            GetFirst(ref target.MaximumVerticalForce) = MaximumVerticalForce;
            Vector2Wide.WriteFirst(TargetVelocity, ref target.TargetVelocity);
            GetFirst(ref target.Depth) = Depth;
            Vector3Wide.WriteFirst(OffsetFromCharacterToSupportPoint, ref target.OffsetFromCharacter);
            Vector3Wide.WriteFirst(OffsetFromSupportToSupportPoint, ref target.OffsetFromSupport);
        }

        /// <summary>Not documented in BEPU source.</summary>
        public void BuildDescription(ref TypeBatch batch, int bundleIndex, int innerIndex, out DynamicCharacterMotionConstraint description)
        {
            ref var source = ref GetOffsetInstance(ref Buffer<DynamicCharacterMotionPrestep>.Get(ref batch.PrestepData, bundleIndex), innerIndex);
            QuaternionWide.ReadFirst(source.SurfaceBasis, out description.SurfaceBasis);
            description.MaximumHorizontalForce = GetFirst(ref source.MaximumHorizontalForce);
            description.MaximumVerticalForce = GetFirst(ref source.MaximumVerticalForce);
            Vector2Wide.ReadFirst(source.TargetVelocity, out description.TargetVelocity);
            description.Depth = GetFirst(ref source.Depth);
            Vector3Wide.ReadFirst(source.OffsetFromCharacter, out description.OffsetFromCharacterToSupportPoint);
            Vector3Wide.ReadFirst(source.OffsetFromSupport, out description.OffsetFromSupportToSupportPoint);
        }
    }

    //Note that all the solver-side data is in terms of 'Wide' data types- the solver never works on just one constraint at a time. Instead,
    //it executes them in bundles of width equal to the runtime/hardware exposed SIMD unit width. This lets the solver scale with wider compute units.
    //(This is important for machines that can perform 8 or more operations per instruction- there's no good way to map a single constraint instance's
    //computation onto such a wide instruction, so if the solver tried to do such a thing, it would leave a huge amount of performance on the table.)

    //"Prestep" data can be thought of as the input to the solver. It describes everything the solver needs to know about.
    /// <summary>AOSOA formatted bundle of prestep data for multiple dynamic-supported character motion constraints.</summary>
    public struct DynamicCharacterMotionPrestep
    {
        //Note that the prestep data layout is important. The solver tends to be severely memory bandwidth bound, so using a minimal representation is valuable.
        //That's why the Basis is stored as a quaternion and not a full Matrix- the cost of the arithmetic operations to expand it back into the original matrix form is far less
        //than the cost of loading all the extra lanes of data when scaled up to many cores.
        /// <summary>Not documented in BEPU source.</summary>
        public QuaternionWide SurfaceBasis;
        /// <summary>Not documented in BEPU source.</summary>
        public Vector<float> MaximumHorizontalForce;
        /// <summary>Not documented in BEPU source.</summary>
        public Vector<float> MaximumVerticalForce;
        /// <summary>Not documented in BEPU source.</summary>
        public Vector<float> Depth;
        /// <summary>Not documented in BEPU source.</summary>
        public Vector2Wide TargetVelocity;
        /// <summary>Not documented in BEPU source.</summary>
        public Vector3Wide OffsetFromCharacter;
        /// <summary>Not documented in BEPU source.</summary>
        public Vector3Wide OffsetFromSupport;
    }

    //Using the prestep data plus some current body state, the solver computes the information required to execute velocity iterations. The main purpose of this intermediate data
    //is to describe the projection from body velocities into constraint space impulses, and from constraint space impulses to body velocities again.
    /// <summary>Not documented in BEPU source.</summary>
    public struct DynamicCharacterMotionProjection
    {
        /// <summary>Not documented in BEPU source.</summary>
        public QuaternionWide SurfaceBasis;
        /// <summary>Not documented in BEPU source.</summary>
        public Vector3Wide OffsetFromCharacter;
        /// <summary>Not documented in BEPU source.</summary>
        public Vector3Wide OffsetFromSupport;
        /// <summary>Not documented in BEPU source.</summary>
        public Vector2Wide TargetVelocity;
        /// <summary>Not documented in BEPU source.</summary>
        public Symmetric2x2Wide HorizontalEffectiveMass;
        /// <summary>Not documented in BEPU source.</summary>
        public Vector<float> MaximumHorizontalImpulse;
        /// <summary>Not documented in BEPU source.</summary>
        public BodyInertias InertiaA;
        /// <summary>Not documented in BEPU source.</summary>
        public BodyInertias InertiaB;
        /// <summary>Not documented in BEPU source.</summary>
        public Vector<float> VerticalBiasVelocity;
        /// <summary>Not documented in BEPU source.</summary>
        public Vector<float> VerticalEffectiveMass;
        /// <summary>Not documented in BEPU source.</summary>
        public Vector<float> MaximumVerticalForce;

    }

    /// <summary>Not documented in BEPU source.</summary>
    public struct DynamicCharacterMotionFunctions : IConstraintFunctions<DynamicCharacterMotionPrestep, DynamicCharacterMotionProjection, CharacterMotionAccumulatedImpulse>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ComputeJacobians(in Vector3Wide offsetA, in Vector3Wide offsetB, in QuaternionWide basisQuaternion,
            out Matrix3x3Wide basis,
            out Matrix2x3Wide horizontalAngularJacobianA, out Matrix2x3Wide horizontalAngularJacobianB,

            out Vector3Wide verticalAngularJacobianA, out Vector3Wide verticalAngularJacobianB)
        {
            //Both of the motion constraints are velocity motors, like tangent friction. They don't actually have a position level goal.
            //But if we did want to make such a position level goal, it could be expressed as:
            //dot(basis.X, constrainedPointOnA - constrainedPointOnB) = 0
            //dot(basis.Y, constrainedPointOnA - constrainedPointOnB) <= 0
            //dot(basis.Z, constrainedPointOnA - constrainedPointOnB) = 0
            //Note that the Y axis, corresponding to the vertical motion constraint, is an inequality. It pulls toward the surface, but never pushes away.
            //It also has a separate maximum force and acts on an independent axis; that's why we solve it as a separate constraint.
            //To get a velocity constraint out of these position goals, differentiate with respect to time:
            //d/dt(dot(basis.X, constrainedPointOnA - constrainedPointOnB)) = dot(basis.X, d/dt(constrainedPointOnA - constrainedPointOnB))
            //                                                              = dot(basis.X, a.LinearVelocity + a.AngularVelocity x offsetToConstrainedPointOnA - b.linearVelocity - b.AngularVelocity x offsetToConstrainedPointOnB)
            //Throwing some algebra and identities at it:
            //dot(basis.X, a.LinearVelocity) + dot(basis.X, a.AngularVelocity x offsetToConstrainedPointOnA) + dot(-basis.X, b.LinearVelocity) + dot(basis.X, offsetToConstrainedPointOnB x b.AngularVelocity)
            //dot(basis.X, a.LinearVelocity) + dot(a.AngularVelocity, offsetToConstrainedPointOnA x basis.X) + dot(-basis.X, b.LinearVelocity) + dot(b.AngularVelocity, basis.X x offsetToConstrainedPointOnB)
            //The (transpose) jacobian is the transform that pulls the body velocity into constraint space-
            //and here, we can see that we have an axis being dotted with each component of the velocity. That's gives us the jacobian for that degree of freedom.
            //The same form applies to all three axes of the basis, since they're all doing the same thing (just on different directions and with different force bounds).
            //Note that we don't explicitly output linear jacobians- they are just the axes of the basis, and the linear jacobians of B are just the negated linear jacobians of A.
            Matrix3x3Wide.CreateFromQuaternion(basisQuaternion, out basis);
            Vector3Wide.CrossWithoutOverlap(offsetA, basis.X, out horizontalAngularJacobianA.X);
            Vector3Wide.CrossWithoutOverlap(offsetA, basis.Y, out verticalAngularJacobianA);
            Vector3Wide.CrossWithoutOverlap(offsetA, basis.Z, out horizontalAngularJacobianA.Y);
            Vector3Wide.CrossWithoutOverlap(basis.X, offsetB, out horizontalAngularJacobianB.X);
            Vector3Wide.CrossWithoutOverlap(basis.Y, offsetB, out verticalAngularJacobianB);
            Vector3Wide.CrossWithoutOverlap(basis.Z, offsetB, out horizontalAngularJacobianB.Y);
        }

        /// <summary>Not documented in BEPU source.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Prestep(Bodies bodies, ref TwoBodyReferences bodyReferences, int count, float dt, float inverseDt, ref BodyInertias inertiaA, ref BodyInertias inertiaB, ref DynamicCharacterMotionPrestep prestepData, out DynamicCharacterMotionProjection projection)
        {
            //The motion constraint is split into two parts: the horizontal constraint, and the vertical constraint.
            //The horizontal constraint acts almost exactly like the TangentFriction, but we'll duplicate some of the logic to keep this implementation self-contained.
            ComputeJacobians(prestepData.OffsetFromCharacter, prestepData.OffsetFromSupport, prestepData.SurfaceBasis,
                out _, out var horizontalAngularJacobianA, out var horizontalAngularJacobianB, out var verticalAngularJacobianA, out var verticalAngularJacobianB);

            //I'll omit the details of where this comes from, but you can check out the other constraints or the sorta-tutorial Inequality1DOF constraint to explain the details,
            //plus some other references. The idea is that we need a way to transform the constraint space velocity (that we get from transforming body velocities
            //by the transpose jacobian) into a corrective impulse for the solver iterations. That corrective impulse is then used to update the velocities on each iteration execution.
            //This transform is the 'effective mass', representing the mass felt by the constraint in its local space.
            //In concept, this constraint is actually two separate constraints solved iteratively, so we have two separate such effective mass transforms.
            Symmetric3x3Wide.MatrixSandwich(horizontalAngularJacobianA, inertiaA.InverseInertiaTensor, out var horizontalAngularContributionA);

            Symmetric3x3Wide.MatrixSandwich(horizontalAngularJacobianB, inertiaB.InverseInertiaTensor, out var horizontalAngularContributionB);
            Symmetric2x2Wide.Add(horizontalAngularContributionA, horizontalAngularContributionB, out var inverseHorizontalEffectiveMass);

            //The linear jacobians are unit length vectors, so J * M^-1 * JT is just M^-1.

            var linearContribution = inertiaA.InverseMass + inertiaB.InverseMass;

            inverseHorizontalEffectiveMass.XX += linearContribution;
            inverseHorizontalEffectiveMass.YY += linearContribution;
            Symmetric2x2Wide.InvertWithoutOverlap(inverseHorizontalEffectiveMass, out projection.HorizontalEffectiveMass);

            //Note that many characters will just have zero inverse inertia tensors to prevent them from rotating, so this could be optimized.
            //(Removing a transform wouldn't matter, but avoiding the storage of an inertia tensor in the projection would be useful.)
            //We don't take advantage of this optimization for simplicity, and so that you could use this constraint unchanged in a simulation
            //where the orientation is instead controlled by some other constraint or torque- imagine a game with gravity that points in different directions.
            Symmetric3x3Wide.VectorSandwich(verticalAngularJacobianA, inertiaA.InverseInertiaTensor, out var verticalAngularContributionA);

            Symmetric3x3Wide.VectorSandwich(verticalAngularJacobianB, inertiaB.InverseInertiaTensor, out var verticalAngularContributionB);

            var inverseVerticalEffectiveMass = verticalAngularContributionA + verticalAngularContributionB + linearContribution;
            projection.VerticalEffectiveMass = Vector<float>.One / inverseVerticalEffectiveMass;

            //Note that we still use the packed representation in the projection information, even though we unpacked it in the prestep.
            //The solver iterations will redo that math rather than storing the full jacobians. This saves quite a bit of memory bandwidth.
            //Storing every jacobian (except duplicate linear jacobians) would require 2x3 * 3 + 1x3 * 3 = 27 wide scalars,
            //while storing the quaternion basis and two offsets requires only 4 + 3 + 3 = 10 scalars.

            //(That might sound irrelevant, but on an AVX2 system, 17 extra scalars means 544 extra bytes per solve iteration.
            //If a machine has 40GBps of main memory bandwidth, those extra bytes require ~13.5 nanoseconds.
            //A quad core AVX2 processor could easily perform over 300 instructions in that time. The story only gets more bandwidth-limited
            //as the core count scales up on pretty much all modern processors.)

            //If you're wondering why we're just copying all of this into the projection rather than loading it again from the prestep data and body data,
            //it's (once again) to minimize memory bandwidth and cache misses. By copying it all into one contiguous struct, the solver iterations
            //have effectively optimal cache line efficiency (outside of their body velocity gathers and scatters, but that's unavoidable in this solver type).
            projection.SurfaceBasis = prestepData.SurfaceBasis;
            projection.OffsetFromCharacter = prestepData.OffsetFromCharacter;

            projection.OffsetFromSupport = prestepData.OffsetFromSupport;

            projection.TargetVelocity.X = prestepData.TargetVelocity.X;
            //The surface basis's Z axis points in the opposite direction to the view direction, so negate the target velocity along the Z axis to point it in the expected direction.
            projection.TargetVelocity.Y = -prestepData.TargetVelocity.Y;
            projection.InertiaA = inertiaA;

            projection.InertiaB = inertiaB;

            projection.MaximumHorizontalImpulse = prestepData.MaximumHorizontalForce * dt;
            projection.MaximumVerticalForce = prestepData.MaximumVerticalForce * dt;
            //If the character is deeply penetrating, the vertical motion constraint will allow some separating velocity- just enough for one frame of integration to reach zero depth.
            projection.VerticalBiasVelocity = Vector.Max(Vector<float>.Zero, prestepData.Depth * inverseDt);

            //Note that there are other ways to store constraints efficiently, some of which can actually reduce the amount of compute work required by the solver iterations.
            //Their use depends on the number of DOFs in the constraint and sometimes special properties of specific constraints.
            //For more details, take a look at the Inequality1DOF sample constraint.
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ApplyHorizontalImpulse(in Matrix3x3Wide basis,
            in Matrix2x3Wide angularJacobianA, in Matrix2x3Wide angularJacobianB, in Vector2Wide constraintSpaceImpulse,
            in BodyInertias inertiaA, in BodyInertias inertiaB,

            ref BodyVelocities velocityA, ref BodyVelocities velocityB)
        {
            //Transform the constraint space impulse into world space by using the jacobian and then apply each body's inverse inertia to get the velocity change.
            Vector3Wide.Scale(basis.X, constraintSpaceImpulse.X, out var linearImpulseAX);
            Vector3Wide.Scale(basis.Z, constraintSpaceImpulse.Y, out var linearImpulseAY);
            Vector3Wide.Add(linearImpulseAX, linearImpulseAY, out var linearImpulseA);
            Vector3Wide.Scale(linearImpulseA, inertiaA.InverseMass, out var linearChangeA);
            Vector3Wide.Add(velocityA.Linear, linearChangeA, out velocityA.Linear);

            Vector3Wide.Scale(linearImpulseA, inertiaB.InverseMass, out var negatedLinearChangeB); //Linear jacobians for B are just A's negated linear jacobians.
            Vector3Wide.Subtract(velocityB.Linear, negatedLinearChangeB, out velocityB.Linear);


            Matrix2x3Wide.Transform(constraintSpaceImpulse, angularJacobianA, out var angularImpulseA);
            Symmetric3x3Wide.TransformWithoutOverlap(angularImpulseA, inertiaA.InverseInertiaTensor, out var angularChangeA);
            Vector3Wide.Add(velocityA.Angular, angularChangeA, out velocityA.Angular);

            Matrix2x3Wide.Transform(constraintSpaceImpulse, angularJacobianB, out var angularImpulseB);
            Symmetric3x3Wide.TransformWithoutOverlap(angularImpulseB, inertiaB.InverseInertiaTensor, out var angularChangeB);
            Vector3Wide.Add(velocityB.Angular, angularChangeB, out velocityB.Angular);

        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ApplyVerticalImpulse(in Matrix3x3Wide basis,
            in Vector3Wide angularJacobianA, in Vector3Wide angularJacobianB, in Vector<float> constraintSpaceImpulse,
            in BodyInertias inertiaA, in BodyInertias inertiaB,

            ref BodyVelocities velocityA, ref BodyVelocities velocityB)
        {
            Vector3Wide.Scale(basis.Y, constraintSpaceImpulse, out var linearImpulseA);
            Vector3Wide.Scale(linearImpulseA, inertiaA.InverseMass, out var linearChangeA);
            Vector3Wide.Add(velocityA.Linear, linearChangeA, out velocityA.Linear);

            Vector3Wide.Scale(linearImpulseA, inertiaB.InverseMass, out var negatedLinearChangeB); //Linear jacobians for B are just A's negated linear jacobians.
            Vector3Wide.Subtract(velocityB.Linear, negatedLinearChangeB, out velocityB.Linear);


            Vector3Wide.Scale(angularJacobianA, constraintSpaceImpulse, out var angularImpulseA);
            Symmetric3x3Wide.TransformWithoutOverlap(angularImpulseA, inertiaA.InverseInertiaTensor, out var angularChangeA);
            Vector3Wide.Add(velocityA.Angular, angularChangeA, out velocityA.Angular);

            Vector3Wide.Scale(angularJacobianB, constraintSpaceImpulse, out var angularImpulseB);
            Symmetric3x3Wide.TransformWithoutOverlap(angularImpulseB, inertiaB.InverseInertiaTensor, out var angularChangeB);
            Vector3Wide.Add(velocityB.Angular, angularChangeB, out velocityB.Angular);

        }

        /// <summary>Not documented in BEPU source.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WarmStart(ref BodyVelocities velocityA, ref BodyVelocities velocityB, ref DynamicCharacterMotionProjection projection, ref CharacterMotionAccumulatedImpulse accumulatedImpulse)
        {
            ComputeJacobians(projection.OffsetFromCharacter, projection.OffsetFromSupport, projection.SurfaceBasis,
                out var basis, out var horizontalAngularJacobianA, out var horizontalAngularJacobianB, out var verticalAngularJacobianA, out var verticalAngularJacobianB);
            ApplyHorizontalImpulse(basis, horizontalAngularJacobianA, horizontalAngularJacobianB, accumulatedImpulse.Horizontal, projection.InertiaA, projection.InertiaB, ref velocityA, ref velocityB);
            ApplyVerticalImpulse(basis, verticalAngularJacobianA, verticalAngularJacobianB, accumulatedImpulse.Vertical, projection.InertiaA, projection.InertiaB, ref velocityA, ref velocityB);
        }

        /// <summary>Not documented in BEPU source.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Solve(ref BodyVelocities velocityA, ref BodyVelocities velocityB, ref DynamicCharacterMotionProjection projection, ref CharacterMotionAccumulatedImpulse accumulatedImpulse)
        {
            ComputeJacobians(projection.OffsetFromCharacter, projection.OffsetFromSupport, projection.SurfaceBasis,
                out var basis, out var horizontalAngularJacobianA, out var horizontalAngularJacobianB, out var verticalAngularJacobianA, out var verticalAngularJacobianB);

            //Compute the velocity error by projecting the body velocity into constraint space using the transposed jacobian.
            Vector2Wide horizontalLinearA;
            Vector3Wide.Dot(basis.X, velocityA.Linear, out horizontalLinearA.X);
            Vector3Wide.Dot(basis.Z, velocityA.Linear, out horizontalLinearA.Y);
            Matrix2x3Wide.TransformByTransposeWithoutOverlap(velocityA.Angular, horizontalAngularJacobianA, out var horizontalAngularA);

            Vector2Wide negatedHorizontalLinearB;
            Vector3Wide.Dot(basis.X, velocityB.Linear, out negatedHorizontalLinearB.X);
            Vector3Wide.Dot(basis.Z, velocityB.Linear, out negatedHorizontalLinearB.Y);
            Matrix2x3Wide.TransformByTransposeWithoutOverlap(velocityB.Angular, horizontalAngularJacobianB, out var horizontalAngularB);
            Vector2Wide.Add(horizontalAngularA, horizontalAngularB, out var horizontalAngular);
            Vector2Wide.Subtract(horizontalLinearA, negatedHorizontalLinearB, out var horizontalLinear);
            Vector2Wide.Add(horizontalAngular, horizontalLinear, out var horizontalVelocity);

            Vector2Wide.Subtract(projection.TargetVelocity, horizontalVelocity, out var horizontalConstraintSpaceVelocityChange);
            Symmetric2x2Wide.TransformWithoutOverlap(horizontalConstraintSpaceVelocityChange, projection.HorizontalEffectiveMass, out var horizontalCorrectiveImpulse);

            //Limit the force applied by the horizontal motion constraint. Note that this clamps the *accumulated* impulse applied this time step, not just this one iterations' value.
            var previousHorizontalAccumulatedImpulse = accumulatedImpulse.Horizontal;
            Vector2Wide.Add(accumulatedImpulse.Horizontal, horizontalCorrectiveImpulse, out accumulatedImpulse.Horizontal);
            Vector2Wide.Length(accumulatedImpulse.Horizontal, out var horizontalImpulseMagnitude);
            //Note division by zero guard.
            var scale = Vector.Min(Vector<float>.One, projection.MaximumHorizontalImpulse / Vector.Max(new Vector<float>(1e-16f), horizontalImpulseMagnitude));
            Vector2Wide.Scale(accumulatedImpulse.Horizontal, scale, out accumulatedImpulse.Horizontal);
            Vector2Wide.Subtract(accumulatedImpulse.Horizontal, previousHorizontalAccumulatedImpulse, out horizontalCorrectiveImpulse);

            ApplyHorizontalImpulse(basis, horizontalAngularJacobianA, horizontalAngularJacobianB, horizontalCorrectiveImpulse, projection.InertiaA, projection.InertiaB, ref velocityA, ref velocityB);

            //Same thing for the vertical constraint.
            Vector3Wide.Dot(basis.Y, velocityA.Linear, out var verticalLinearA);
            Vector3Wide.Dot(velocityA.Angular, verticalAngularJacobianA, out var verticalAngularA);

            Vector3Wide.Dot(basis.Y, velocityB.Linear, out var negatedVerticalLinearB);
            Vector3Wide.Dot(velocityB.Angular, verticalAngularJacobianB, out var verticalAngularB);
            //The vertical constraint just targets zero velocity, but does not attempt to fight any velocity which would merely push the character out of penetration.
            var verticalCorrectiveImpulse = (projection.VerticalBiasVelocity - verticalLinearA + negatedVerticalLinearB - verticalAngularA - verticalAngularB) * projection.VerticalEffectiveMass;

            //Clamp the vertical constraint's impulse, but note that this is a bit different than above- the vertical constraint is not allowed to *push*, so there's an extra bound at zero.
            var previousVerticalAccumulatedImpulse = accumulatedImpulse.Vertical;
            accumulatedImpulse.Vertical = Vector.Min(Vector<float>.Zero, Vector.Max(accumulatedImpulse.Vertical + verticalCorrectiveImpulse, -projection.MaximumVerticalForce));
            verticalCorrectiveImpulse = accumulatedImpulse.Vertical - previousVerticalAccumulatedImpulse;

            ApplyVerticalImpulse(basis, verticalAngularJacobianA, verticalAngularJacobianB, verticalCorrectiveImpulse, projection.InertiaA, projection.InertiaB, ref velocityA, ref velocityB);
        }

    }

    //Each constraint type has its own 'type processor'- it acts as the outer loop that handles all the common logic across batches of constraints and invokes
    //the per-constraint logic as needed. The CharacterMotionFunctions type provides the actual implementation.
    /// <summary>Not documented in BEPU source.</summary>
    public class DynamicCharacterMotionTypeProcessor : TwoBodyTypeProcessor<DynamicCharacterMotionPrestep, DynamicCharacterMotionProjection, CharacterMotionAccumulatedImpulse, DynamicCharacterMotionFunctions>
    {
        /// <summary>
        /// Simulation-wide unique id for the character motion constraint. Every type has needs a unique compile time id; this is a little bit annoying to guarantee given that there is no central
        /// registry of all types that can exist (custom ones, like this one, can always be created), but having it be constant helps simplify and optimize its internal usage.
        /// </summary>
        public const int BatchTypeId = 51;
    }
}
