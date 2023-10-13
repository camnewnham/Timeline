﻿using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Plugin
{

    public abstract class Sequence
    {
        public abstract string Name { get; }
        public int KeyframeCount => keyframes.Count;
        private readonly HashSet<Keyframe> keyframes = new HashSet<Keyframe>();

        private List<Keyframe> orderedKeyframes;

        public IEnumerable<Keyframe> Keyframes => keyframes;

        public List<Keyframe> OrderedKeyframes
        {
            get
            {
                if (orderedKeyframes == null)
                {
                    orderedKeyframes = keyframes.OrderBy(x => x.Time).ToList();
                }
                return orderedKeyframes;
            }
        }


        public bool IsEmpty => keyframes.Count == 0;


        public void AddKeyframe(Keyframe keyframe)
        {
            orderedKeyframes = null;
            _ = keyframes.RemoveWhere(x => x.Time == keyframe.Time);
            _ = keyframes.Add(keyframe);
        }

        public abstract bool SetTime(double time, GH_Document doc);
    }

    public class CameraSequence : Sequence
    {
        public override string Name => "Camera";

        public override bool SetTime(double time, GH_Document doc)
        {
            // tODO
            return false;
        }
    }

    public class ComponentSequence : Sequence
    {
        private GH_Document m_document;
        public GH_Document Document
        {
            get => m_document;
            internal set
            {
                if (value != m_document)
                {
                    m_document = value;
                    m_ghObject = null;
                }
            }
        }
        public IGH_DocumentObject DocumentObject
        {
            get
            {
                if (m_ghObject == null)
                {
                    m_ghObject = Document?.FindObject(InstanceGuid, true);

                    if (m_ghObject == null)
                    {
                        throw new KeyNotFoundException($"Unable to find document object to restore state ({InstanceGuid})");
                    }
                }
                return m_ghObject;
            }
        }

        public override string Name => DocumentObject?.GetName() ?? "Component";
        /// <summary>
        /// Hashset of the last state. The component corresponding to this sequence will
        /// only be expired if the hashcode changes.
        /// </summary>
        private int LastStateHashCode = -1;

        private IGH_DocumentObject m_ghObject;

        private Guid m_instanceGuid;
        public Guid InstanceGuid
        {
            get => m_instanceGuid;
            set
            {
                if (value != m_instanceGuid)
                {
                    m_instanceGuid = value;
                    m_ghObject = null;
                }
            }
        }

        public ComponentSequence(Guid instanceGuid, GH_Document doc)
        {
            InstanceGuid = instanceGuid;
            Document = doc;
        }

        /// <summary>
        /// Handles interpolation of keyframes in this sequence, expiring components as required
        /// </summary>
        /// <param name="time">The time to set</param>
        /// <param name="doc">The document to apply the time change to</param>
        /// <returns>True if setting the time resulted in a change and expired the component (that should prompt component and document expiry)</returns>
        public override bool SetTime(double time, GH_Document doc)
        {
            Document = doc;
            int oldHash = LastStateHashCode;
            for (int i = 0; i < OrderedKeyframes.Count; i++)
            {
                ComponentKeyframe current = OrderedKeyframes[i] as ComponentKeyframe;
                ComponentKeyframe next = i < OrderedKeyframes.Count - 1 ? OrderedKeyframes[i + 1] as ComponentKeyframe : null;

                if (next == null // Past last frame or only one keyframe
                    || (i == 0 && current.Time >= time))   // On or before first frame
                {
                    LastStateHashCode = current.LoadState(DocumentObject);
                    break;
                }
                else if (time >= current.Time && time < next.Time) // Between this frame and next 
                {
                    double fraction = MathUtils.Remap(time, current.Time, next.Time, 0, 1);
                    LastStateHashCode = current.InterpolateState(DocumentObject, next, fraction);
                    break;
                }
            }

            if (oldHash != LastStateHashCode)
            {
                if (DocumentObject is IGH_ActiveObject activeObj && activeObj.Phase != GH_SolutionPhase.Blank)
                {
                    DocumentObject.ExpireSolution(false);
                }
            }

            return oldHash != LastStateHashCode;
        }
    }
}
