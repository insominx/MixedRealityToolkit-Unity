﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.MixedReality.Toolkit.Input;
using Microsoft.MixedReality.Toolkit.UI;
using UnityEditor;
using UnityEngine;

namespace Microsoft.MixedReality.Toolkit.Editor
{
    [CustomEditor(typeof(PressableButton))]
    public class PressableButtonInspector : UnityEditor.Editor
    {
        // Struct used to store state of preview.
        // This lets us display accurate info while button is being pressed.
        // All vectors / distances are in local space.
        private struct ButtonInfo
        {
            // Convenience fields for box collider info
            public Bounds TouchCageLocalBounds;

            // The ray along which the button is pushed.
            public Ray PushRayLocal;
            // The rotation of the push space.
            public Quaternion PushRotationLocal;

            // The actual values that the button uses
            public float StartPushDistance;
            public float MaxPushDistance;
            public float PressDistance;
            public float ReleaseDistance;
        }

        const string EditingEnabledKey = "MRTK_PressableButtonInspector_EditingEnabledKey";
        const string VisiblePlanesKey = "MRTK_PressableButtonInspector_VisiblePlanesKey";
        private static bool EditingEnabled = false;
        private static bool VisiblePlanes = true;

        private const float labelMouseOverDistance = 0.025f;

        private static GUIStyle labelStyle;

        private PressableButton button;
        private Transform transform;
        private BoxCollider touchCage;
        private NearInteractionTouchable touchable;

        private ButtonInfo currentInfo;

        private SerializedProperty startPushDistance;
        private SerializedProperty maxPushDistance;
        private SerializedProperty pressDistance;
        private SerializedProperty releaseDistanceDelta;

        private SerializedProperty movingButtonVisuals;
        private SerializedProperty useLocalSpaceDistances;

        private static Vector3[] startPlaneVertices = new Vector3[4];
        private static Vector3[] endPlaneVertices = new Vector3[4];
        private static Vector3[] pressPlaneVertices = new Vector3[4];
        private static Vector3[] pressStartPlaneVertices = new Vector3[4];
        private static Vector3[] releasePlaneVertices = new Vector3[4];

        private void OnEnable()
        {
            button = (PressableButton)target;
            transform = button.transform;

            touchCage = button.GetComponent<BoxCollider>();

            if (labelStyle == null)
            {
                labelStyle = new GUIStyle();
                labelStyle.normal.textColor = Color.white;
            }

            startPushDistance = serializedObject.FindProperty("startPushDistance");
            maxPushDistance = serializedObject.FindProperty("maxPushDistance");
            pressDistance = serializedObject.FindProperty("pressDistance");
            releaseDistanceDelta = serializedObject.FindProperty("releaseDistanceDelta");
            movingButtonVisuals = serializedObject.FindProperty("movingButtonVisuals");
            useLocalSpaceDistances = serializedObject.FindProperty("useLocalSpaceDistances");

            touchable = button.GetComponent<NearInteractionTouchable>();
        }

        [DrawGizmo(GizmoType.Selected)]
        private void OnSceneGUI()
        {
            if (touchCage == null)
            {
                return;
            }

            if (!VisiblePlanes)
            {
                return;
            }

            // If the button is being pressed, don't gather new info
            // Just display the info we already gathered
            // This lets people view button presses in real-time
            if (button.IsTouching)
            {
                DrawButtonInfo(currentInfo, false);
            }
            else
            {
                currentInfo = GatherCurrentInfo();
                DrawButtonInfo(currentInfo, EditingEnabled);
            }
        }

        private ButtonInfo GatherCurrentInfo()
        {
            ButtonInfo info = new ButtonInfo();

            info.TouchCageLocalBounds = new Bounds(touchCage.center, touchCage.size);

            Vector3 pressDirLocal = (touchable != null) ? -1.0f * touchable.LocalForward : Vector3.forward;
            Vector3 upDirLocal = (touchable != null) ? touchable.LocalUp : Vector3.up;

            info.PushRotationLocal = Quaternion.LookRotation(pressDirLocal, upDirLocal);

            // All the planes should be drawn within the button cage. But the distance planes are relative to the 
            // initial transform projected onto the box collider.
            Vector3 initialPositionLocal = transform.InverseTransformPoint(button.InitialPosition) ;
            
            // Project the initial position onto the ray that goes through the touch cage center.
            Vector3 initialPosLocal = Vector3.Project(initialPositionLocal - info.TouchCageLocalBounds.center, pressDirLocal) + info.TouchCageLocalBounds.center;
            info.PushRayLocal = new Ray(initialPosLocal, pressDirLocal);

            bool useLocalSpace = useLocalSpaceDistances.boolValue;
            info.StartPushDistance = useLocalSpace ? startPushDistance.floatValue : startPushDistance.floatValue / transform.lossyScale.z;
            info.MaxPushDistance = useLocalSpace ? maxPushDistance.floatValue : maxPushDistance.floatValue / transform.lossyScale.z;
            info.PressDistance = useLocalSpace ? pressDistance.floatValue : pressDistance.floatValue / transform.lossyScale.z;
            info.ReleaseDistance = useLocalSpace ? pressDistance.floatValue - releaseDistanceDelta.floatValue : (pressDistance.floatValue - releaseDistanceDelta.floatValue) / transform.lossyScale.z;

            return info;
        }

        private void DrawButtonInfo(ButtonInfo info, bool editingEnabled)
        {
            if (editingEnabled)
            {
                EditorGUI.BeginChangeCheck();
            }

            // START PUSH
            Handles.color = Color.cyan;
            float newStartPushDistance = DrawPlaneAndHandle(startPlaneVertices, info.TouchCageLocalBounds.size * 0.5f, info.StartPushDistance, info, "Start Push Distance", editingEnabled);
            if (editingEnabled && newStartPushDistance != info.StartPushDistance)
            {
                EnforceDistanceOrdering(ref info);
                info.StartPushDistance = Mathf.Min(newStartPushDistance, info.ReleaseDistance);
            }

            // RELEASE DISTANCE
            Handles.color = Color.red;
            float newReleaseDistance = DrawPlaneAndHandle(releasePlaneVertices, info.TouchCageLocalBounds.size * 0.3f, info.ReleaseDistance, info, "Release Distance", editingEnabled);
            if (editingEnabled && newReleaseDistance != info.ReleaseDistance)
            {
                EnforceDistanceOrdering(ref info);
                info.ReleaseDistance = Mathf.Clamp(newReleaseDistance, info.StartPushDistance, info.PressDistance);
            }

            // PRESS DISTANCE
            Handles.color = Color.yellow;
            float newPressDistance = DrawPlaneAndHandle(pressPlaneVertices, info.TouchCageLocalBounds.size * 0.35f, info.PressDistance, info, "Press Distance", editingEnabled);
            if (editingEnabled && newPressDistance != info.PressDistance)
            {
                EnforceDistanceOrdering(ref info);
                info.PressDistance = Mathf.Clamp(newPressDistance, info.ReleaseDistance, info.MaxPushDistance);
            }

            // MAX PUSH
            Handles.color = Color.cyan;
            float newMaxPushDistance = DrawPlaneAndHandle(endPlaneVertices, info.TouchCageLocalBounds.size * 0.5f, info.MaxPushDistance, info, "Max Push Distance", editingEnabled);
            if (editingEnabled && newMaxPushDistance != info.MaxPushDistance)
            {
                EnforceDistanceOrdering(ref info);
                info.MaxPushDistance = Mathf.Max(newMaxPushDistance, info.PressDistance);
            }

            /// {dahof} NOTE: collider box editing is disabled as we would need to find the min / max point of it along the press direction, which is arbitrary.
            /// I think for simplicity reasons, we should make editing the collider independent from editing the PressableButton distances.

            // BUTTON CONTENT ORIGIN
            // Don't allow editing of button position
            /*Handles.color = Color.green;
            DrawPlaneAndHandle(pressStartPlaneVertices, info.TouchCageLocalBounds.size * 0.4f, newInfo.StartPos, info.TouchStartOrigin, "Moving button visuals", false);
            // START POINT
            // Start point doesn't need a display offset because it's based on the touch cage center
            Handles.color = Color.cyan;
            newInfo.TouchStartPos = DrawPlaneAndHandle(startPlaneVertices, info.TouchCageLocalBounds.size * 0.5f, newInfo.TouchStartPos, info.TouchStartOrigin, "Touch event", editingEnabled);
            if (editingEnabled)
            {
                // The touch event is defined by the collider bounds
                // If we've moved the start pos, we've moved the bounds
                float difference = (info.TouchStartPos - newInfo.TouchStartPos);
                if (Mathf.Abs(difference) > 0)
                {
                    newInfo.TouchCageCenter -= difference / 2;
                    newInfo.TouchCageSize += difference;
                }
            }*/

            if (editingEnabled && EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(target, "Modify PressableButton");

                startPushDistance.floatValue = useLocalSpaceDistances.boolValue ? info.StartPushDistance : info.StartPushDistance * transform.lossyScale.z;
                maxPushDistance.floatValue = useLocalSpaceDistances.boolValue ? info.MaxPushDistance : info.MaxPushDistance * transform.lossyScale.z; 
                pressDistance.floatValue = useLocalSpaceDistances.boolValue ? info.PressDistance : info.PressDistance * transform.lossyScale.z;
                releaseDistanceDelta.floatValue = useLocalSpaceDistances.boolValue ? info.PressDistance - info.ReleaseDistance : (info.PressDistance - info.ReleaseDistance) * transform.lossyScale.z;

                /*boxColliderSize.vector3Value = new Vector3(info.TouchCageLocalBounds.size.x, info.TouchCageLocalBounds.size.y, newInfo.TouchCageSize);
                boxColliderCenter.vector3Value = new Vector3(info.TouchCageLocalBounds.center.x, info.TouchCageLocalBounds.center.y, newInfo.TouchCageCenter);
                boxColliderObject.ApplyModifiedProperties();*/

                serializedObject.ApplyModifiedProperties();
            }

            // Draw dotted lines showing path from beginning to end of button path
            Handles.color = Color.Lerp(Color.cyan, Color.clear, 0.25f);
            Handles.DrawDottedLine(startPlaneVertices[0], endPlaneVertices[0], 2.5f);
            Handles.DrawDottedLine(startPlaneVertices[1], endPlaneVertices[1], 2.5f);
            Handles.DrawDottedLine(startPlaneVertices[2], endPlaneVertices[2], 2.5f);
            Handles.DrawDottedLine(startPlaneVertices[3], endPlaneVertices[3], 2.5f);
        }

        private void EnforceDistanceOrdering(ref ButtonInfo info)
        {
            info.StartPushDistance = Mathf.Min(new[] { info.StartPushDistance, info.ReleaseDistance, info.PressDistance, info.MaxPushDistance });
            info.ReleaseDistance = Mathf.Min(new[] { info.ReleaseDistance, info.PressDistance, info.MaxPushDistance });
            info.PressDistance = Mathf.Min(info.PressDistance, info.MaxPushDistance);
        }

        private float DrawPlaneAndHandle(Vector3[] vertices, Vector3 halfExtents, float distance, ButtonInfo info, string label, bool editingEnabled)
        {
            Vector3 centerLocal = info.PushRayLocal.GetPoint(distance);
            MakeQuadFromPoint(vertices, centerLocal, halfExtents, info);

            if (VisiblePlanes)
            {
                Handles.DrawSolidRectangleWithOutline(vertices, Color.Lerp(Handles.color, Color.clear, 0.65f), Handles.color);
            }

            // Label
            {
                Vector3 mousePosition = SceneView.currentDrawingSceneView.camera.ScreenToViewportPoint(Event.current.mousePosition);
                mousePosition.y = 1f - mousePosition.y;
                mousePosition.z = 0;
                Vector3 handleVisiblePos = SceneView.currentDrawingSceneView.camera.WorldToViewportPoint(vertices[1]);
                handleVisiblePos.z = 0;

                if (Vector3.Distance(mousePosition, handleVisiblePos) < labelMouseOverDistance)
                {
                    DrawLabel(vertices[1], transform.up - transform.right, label, labelStyle);
                    HandleUtility.Repaint();
                }
            }

            // Draw forward / backward arrows so people know they can drag
            if (editingEnabled)
            {
                float handleSize = HandleUtility.GetHandleSize(vertices[1]) * 0.15f;

                Vector3 planeNormal = button.transform.TransformDirection(info.PushRayLocal.direction);
                Handles.ArrowHandleCap(0, vertices[1], Quaternion.LookRotation(planeNormal), handleSize * 2, EventType.Repaint);
                Handles.ArrowHandleCap(0, vertices[1], Quaternion.LookRotation(-planeNormal), handleSize * 2, EventType.Repaint);

                Vector3 newPosition = Handles.FreeMoveHandle(vertices[1], Quaternion.identity, handleSize, Vector3.zero, Handles.SphereHandleCap);
                if (!newPosition.Equals(vertices[1]))
                {
                    Vector3 newCenterLocal = button.transform.InverseTransformPoint(newPosition);
                    distance = Vector3.Dot(newCenterLocal - info.PushRayLocal.origin, info.PushRayLocal.direction);
                }
            }

            return distance;
        }

        /// <summary>
        /// Trigger function for plane distance world to/from local space conversion
        /// </summary>
        public void onTriggerPlaneDistanceConversion()
        {
            useLocalSpaceDistances.boolValue = !useLocalSpaceDistances.boolValue;
            Vector3 worldPressDir = touchable == null ? transform.forward : touchable.Forward * -1.0f;
            float worldToLocalScale = transform.InverseTransformVector(worldPressDir).magnitude;

            Undo.RecordObject(target, "Modify PressableButton");

            if (!useLocalSpaceDistances.boolValue)
            {
                startPushDistance.floatValue /= worldToLocalScale;
                maxPushDistance.floatValue /= worldToLocalScale;
                pressDistance.floatValue /= worldToLocalScale;
                releaseDistanceDelta.floatValue /= worldToLocalScale;
            }
            else
            {
                startPushDistance.floatValue *= worldToLocalScale;
                maxPushDistance.floatValue *= worldToLocalScale;
                pressDistance.floatValue *= worldToLocalScale;
                releaseDistanceDelta.floatValue *= worldToLocalScale;
            }
            serializedObject.ApplyModifiedProperties();

            currentInfo = GatherCurrentInfo();
            DrawButtonInfo(currentInfo, EditingEnabled);
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            bool useLocalDistances = useLocalSpaceDistances.boolValue;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(useLocalDistances ? "Plane Distances are in local space" : "Plane Distances are in world space", EditorStyles.boldLabel);
            if (GUILayout.Button(useLocalDistances ? "Convert Distances to World Space" : "Convert Distances to Local Space") && EditingEnabled)
            {
                onTriggerPlaneDistanceConversion();
            }

            if (Application.isPlaying)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Button State", EditorStyles.boldLabel);
                EditorGUILayout.Toggle("Touching", button.IsTouching);
                EditorGUILayout.Toggle("Pressing", button.IsPressing);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Editor Settings", EditorStyles.boldLabel);
            VisiblePlanes = SessionState.GetBool(VisiblePlanesKey, true);
            bool newValue = EditorGUILayout.Toggle("Show Button Event Planes", VisiblePlanes);
            if (newValue != VisiblePlanes)
            {
                SessionState.SetBool(VisiblePlanesKey, newValue);
            }

            if (VisiblePlanes)
            {
                EditingEnabled = SessionState.GetBool(EditingEnabledKey, false);
                newValue = EditorGUILayout.Toggle("Make Planes Editable", EditingEnabled);
                if (newValue != EditingEnabled)
                {
                    SessionState.SetBool(EditingEnabledKey, newValue);
                    EditorUtility.SetDirty(target);
                }
            }
        }

        private bool IsMouseOverQuad(ButtonInfo info, Vector3 halfExtents, Vector3 centerLocal)
        {
            Vector3 mousePosition = Event.current.mousePosition;
            mousePosition.y = SceneView.currentDrawingSceneView.camera.pixelHeight - mousePosition.y;
            Ray mouseRay = SceneView.currentDrawingSceneView.camera.ScreenPointToRay(mousePosition);

            // Transform to local object space.
            mouseRay.direction = button.transform.InverseTransformDirection(mouseRay.direction);
            mouseRay.origin = button.transform.InverseTransformPoint(mouseRay.origin);

            // Transform to plane space, which transform the plane into the XY plane.
            Quaternion quadRotationInverse = Quaternion.Inverse(info.PushRotationLocal);
            mouseRay.direction = quadRotationInverse * mouseRay.direction;
            mouseRay.origin = quadRotationInverse * (mouseRay.origin - centerLocal);

            // Intersect ray with XY plane.
            Plane xyPlane = new Plane(Vector3.forward, 0.0f);
            float intersectionDistance = 0.0f;
            if (xyPlane.Raycast(mouseRay, out intersectionDistance))
            {
                Vector3 intersection = mouseRay.GetPoint(intersectionDistance);
                return (Mathf.Abs(intersection.x) <= halfExtents.x && Mathf.Abs(intersection.y) <= halfExtents.y);
            }

            return false;
        }

        private void DrawLabel(Vector3 origin, Vector3 direction, string content, GUIStyle labelStyle)
        {
            Color colorOnEnter = Handles.color;

            float handleSize = HandleUtility.GetHandleSize(origin);
            Vector3 handlePos = origin + direction.normalized * handleSize * 2;
            Handles.Label(handlePos + (Vector3.up * handleSize * 0.1f), content, labelStyle);
            Handles.color = Color.Lerp(colorOnEnter, Color.clear, 0.25f);
            Handles.DrawDottedLine(origin, handlePos, 5f);

            Handles.color = colorOnEnter;
        }

        private void MakeQuadFromPoint(Vector3[] vertices, Vector3 centerLocal, Vector3 halfExtents, ButtonInfo info)
        {
            vertices[0] = button.transform.TransformPoint((info.PushRotationLocal * new Vector3(-halfExtents.x, -halfExtents.y, 0.0f)) + centerLocal);
            vertices[1] = button.transform.TransformPoint((info.PushRotationLocal * new Vector3(-halfExtents.x, +halfExtents.y, 0.0f)) + centerLocal);
            vertices[2] = button.transform.TransformPoint((info.PushRotationLocal * new Vector3(+halfExtents.x, +halfExtents.y, 0.0f)) + centerLocal);
            vertices[3] = button.transform.TransformPoint((info.PushRotationLocal * new Vector3(+halfExtents.x, -halfExtents.y, 0.0f)) + centerLocal);
        }
    }
}