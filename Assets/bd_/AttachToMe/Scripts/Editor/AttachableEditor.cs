/*
 * Copyright (c) 2021 bd_
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the
 * "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish,
 * distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to
 * the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY
 * CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
 * SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 */

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UdonSharpEditor;

namespace net.fushizen.attachable
{
    [CustomEditor(typeof(AttachableConfig))]
    [CanEditMultipleObjects]
    class AttachableConfigEditor : AttachableEditor
    {
    }
    
    [CustomEditor(typeof(Attachable))]
    [CanEditMultipleObjects]
    class AttachableEditor : Editor
    {
        static bool lang_jp = false;
        bool show_general = true;
        bool show_selection = true;
        bool show_perms = true;
        bool show_animator = false;

        SerializedProperty m_t_pickup;
        SerializedProperty m_t_attachmentDirection;

        SerializedProperty m_range;
        SerializedProperty m_directionality;
        SerializedProperty m_disableFingerSelection;
        SerializedProperty m_respawnTime;

        SerializedProperty m_perm_removeTracee;
        SerializedProperty m_perm_removeOwner;
        SerializedProperty m_perm_removeOther;

        SerializedProperty m_c_animator;
        SerializedProperty m_anim_onTrack, m_anim_onTrackLocal, m_anim_onHeld, m_anim_onHeldLocal;

        struct Strings
        {
            public GUIContent btn_langSwitch;

            public GUIContent header_general;

            public GUIContent m_t_pickup;

            public GUIContent header_selection;
            public GUIContent m_range;
            public GUIContent m_disableFingerTracking;
            public GUIContent m_respawnTime;

            public GUIContent m_t_attachmentDirection;
            public GUIContent m_directionality;

            public GUIContent header_pickup_perms;
            public GUIContent header_pickup_perms_2;
            public GUIContent header_pickup_perms_3;

            public GUIContent m_perm_removeTracee;
            public GUIContent m_perm_removeOwner;
            public GUIContent m_perm_removeOther;

            public GUIContent header_animator;
            public GUIContent m_c_animator;
            public GUIContent header_animator_flags;
            public GUIContent m_anim_onTrack, m_anim_onTrackLocal;
            public GUIContent m_anim_onHeld, m_anim_onHeldLocal;

            public GUIContent header_internal;

            public GUIContent warn_direction, warn_missing;

            public GUIContent m_trackOnUpdate;
        }

        Strings en, jp;

        void LoadStrings()
        {
            jp = new Strings()
            {
                btn_langSwitch = new GUIContent("Switch to English", "Switch language"),
                header_general = new GUIContent("基本設定"),
                m_t_pickup = new GUIContent("ピックアップオブジェクト"),
                header_selection = new GUIContent("選択設定"),
                m_range = new GUIContent("ボーン選択範囲", "この範囲内のボーンが選択対象になります"),
                m_directionality = new GUIContent("指向性", "この数値をゼロ以上にすると、【方向マーカー】に指定されたオブジェクトのZ+方向の先にあるボーンを優先的に選択します。"),
                m_t_attachmentDirection = new GUIContent("方向マーカー", "このオブジェクトのZ+方向と位置を基準にボーンの選択を行います"),
                m_disableFingerTracking = new GUIContent("指ボーンに追従しない"),
                m_respawnTime = new GUIContent("リスポーン時間", "設定時間以上放置されていると、初期地点に戻ります。秒数で入力、ゼロで無効化できます。"),
                warn_direction = new GUIContent("方向マーカーをプレハブの子に設定してください"),
                header_pickup_perms = new GUIContent("取り外しできるプレイヤーの設定"),
                header_pickup_perms_2 = new GUIContent("追尾しているピックアップを取り外せるプレイヤーを設定できます。"),
                header_pickup_perms_3 = new GUIContent("追尾していない場合はだれでも手にとれます。"),
                m_perm_removeTracee = new GUIContent("追尾対象プレイヤー自身"),
                m_perm_removeOwner = new GUIContent("最後に触った人"),
                m_perm_removeOther = new GUIContent("その他の人"),
                header_internal = new GUIContent("内部設定"),
                warn_missing = new GUIContent("必須項目です。"),
                header_animator = new GUIContent("Animator連動設定"),
                m_c_animator = new GUIContent("連動させるAnimator"),
                header_animator_flags = new GUIContent("フラグパラメーター名"),
                m_anim_onTrack = new GUIContent("トラッキング中フラグ"),
                m_anim_onTrackLocal = new GUIContent("ローカルプレイヤーをﾄﾗｯｷﾝｸﾞ中"),
                m_anim_onHeld = new GUIContent("誰かが持っているフラグ"),
                m_anim_onHeldLocal = new GUIContent("ローカルで持っているフラグ"),

                m_trackOnUpdate = new GUIContent("処理タイミングをずらす", "Dynamic Bone等物理演算がぷるぷるするときはチェック入れましょう（少しラグが発生します）"),
            };

            en = new Strings()
            {
                btn_langSwitch = new GUIContent("日本語に切り替え", "Switch language"),
                header_general = new GUIContent("General settings"),
                m_t_pickup = new GUIContent("Pickup object"),
                header_selection = new GUIContent("Selection configuration"),
                m_range = new GUIContent("Bone selection radius", "Bones within this radius will be considered as potential targets"),
                m_directionality = new GUIContent("Directionality", "If you set this value above zero, "),
                m_t_attachmentDirection = new GUIContent("Direction marker", "This object's Z+ direction and position will be used as the basis for bone selection."),
                m_disableFingerTracking = new GUIContent("Disable finger bone tracking"),
                m_respawnTime = new GUIContent("Respawn time ", "If the prop is left idle for longer than this time, it will return to its initial position. Expressed in seconds, zero to disable."),
                warn_direction = new GUIContent("Please ensure that the direction marker is a child of the pickup object."),
                header_pickup_perms = new GUIContent("Removal permissions"),
                header_pickup_perms_2 = new GUIContent("Select which players can remove a tracking pickup."),
                header_pickup_perms_3 = new GUIContent("When not tracking a bone, anyone can pick up the pickup."),
                m_perm_removeTracee = new GUIContent("Person being tracked can remove"),
                m_perm_removeOwner = new GUIContent("Last person to touch pickup can remove"),
                m_perm_removeOther = new GUIContent("Anyone else can remove"),
                header_internal = new GUIContent("Internal settings"),
                warn_missing = new GUIContent("This field is required"),
                header_animator = new GUIContent("Animator control configuration"),
                m_c_animator = new GUIContent("Animator to control"),
                header_animator_flags = new GUIContent("Flag parameter names"),
                m_anim_onTrack = new GUIContent("Tracking bone"),
                m_anim_onTrackLocal = new GUIContent("Tracking the local player's bone"),
                m_anim_onHeld = new GUIContent("Held in hand"),
                m_anim_onHeldLocal = new GUIContent("Held by local player"),

                m_trackOnUpdate = new GUIContent("Alternate timing", "Check this if a dynamic bone or other physics object vibrates when moving " +
                    "(this will result in some additional lag, only check it if you need it!)"),
            };
        }

        private void OnEnable()
        {
            LoadStrings();

            m_t_pickup = serializedObject.FindProperty("t_pickup");
            m_t_attachmentDirection = serializedObject.FindProperty("t_attachmentDirection");

            m_range = serializedObject.FindProperty("range");
            m_directionality = serializedObject.FindProperty("directionality");
            m_disableFingerSelection = serializedObject.FindProperty(nameof(Attachable.disableFingerSelection));
            m_respawnTime = serializedObject.FindProperty("respawnTime");

            m_perm_removeTracee = serializedObject.FindProperty("perm_removeTracee");
            m_perm_removeOwner = serializedObject.FindProperty("perm_removeOwner");
            m_perm_removeOther = serializedObject.FindProperty("perm_removeOther");

            m_c_animator = serializedObject.FindProperty("c_animator");
            m_anim_onHeld = serializedObject.FindProperty("anim_onHeld");
            m_anim_onHeldLocal = serializedObject.FindProperty("anim_onHeldLocal");
            m_anim_onTrack = serializedObject.FindProperty("anim_onTrack");
            m_anim_onTrackLocal = serializedObject.FindProperty("anim_onTrackLocal");
        }

        public override void OnInspectorGUI()
        {
            bool isMultiple = targets.Length > 1;

            var lang = lang_jp ? jp : en;

            bool switchLanguage = GUILayout.Button(lang.btn_langSwitch);
            if (switchLanguage)
            {
                lang_jp = !lang_jp;
            }

            Transform t_pickup = null;

            EditorGUILayout.Space();
            show_general = EditorGUILayout.Foldout(show_general, lang.header_general);

            if (show_general)
            {
                if (!isMultiple)
                {
                    EditorGUILayout.PropertyField(m_t_pickup, lang.m_t_pickup);


                }
            }

            t_pickup = (Transform)m_t_pickup.objectReferenceValue;
            if (t_pickup == null)
            {
                if (!show_general)
                {
                    show_general = true;
                    EditorUtility.SetDirty(target);
                }
                EditorGUILayout.HelpBox(lang.warn_missing.text, MessageType.Error);
            }

            if (show_general)
            {
                // trackOnUpdate seems unnecessary with OnPostLateUpdate update loops.
                // Leaving it in (available in the debug inspector) just in case for now.
                //EditorGUILayout.PropertyField(m_trackOnUpdate, lang.m_trackOnUpdate);
                EditorGUILayout.PropertyField(m_respawnTime, lang.m_respawnTime);

                EditorGUILayout.Space();
            }

            show_selection = EditorGUILayout.Foldout(show_selection, lang.header_selection);
            if (show_selection)
            {
                EditorGUILayout.PropertyField(m_range, lang.m_range);
                if (!isMultiple) EditorGUILayout.PropertyField(m_t_attachmentDirection, lang.m_t_attachmentDirection);
            }

            if (!isMultiple)
            {
                var t_direction = (Transform)m_t_attachmentDirection.objectReferenceValue;
                if (t_direction == null)
                {
                    if (!show_selection)
                    {
                        show_selection = true;
                        EditorUtility.SetDirty(target);
                    }
                    EditorGUILayout.HelpBox(lang.warn_missing.text, MessageType.Error);
                } else
                {
                    var trace = t_direction.parent;
                    while (trace != null && trace != t_pickup)
                    {
                        trace = trace.parent;
                    }

                    if (trace == null)
                    {
                        if (!show_selection)
                        {
                            show_selection = true;
                            EditorUtility.SetDirty(target);
                        }
                        EditorGUILayout.HelpBox(lang.warn_direction.text, MessageType.Warning);
                    }
                }
            }

            if (show_selection)
            {
                EditorGUILayout.PropertyField(m_directionality, lang.m_directionality);
                EditorGUILayout.PropertyField(m_disableFingerSelection, lang.m_disableFingerTracking);
            }
            
            EditorGUILayout.Space();

            show_perms = EditorGUILayout.Foldout(show_perms, lang.header_pickup_perms);
            if (show_perms)
            {
                EditorGUILayout.LabelField(lang.header_pickup_perms_2);
                EditorGUILayout.LabelField(lang.header_pickup_perms_3);
                EditorGUILayout.Space();


                EditorGUILayout.PropertyField(m_perm_removeTracee, lang.m_perm_removeTracee);
                EditorGUILayout.PropertyField(m_perm_removeOwner, lang.m_perm_removeOwner);
                EditorGUILayout.PropertyField(m_perm_removeOther, lang.m_perm_removeOther);
            }

            show_animator = EditorGUILayout.Foldout(show_animator, lang.header_animator);
            if (show_animator)
            {
                EditorGUILayout.PropertyField(m_c_animator, lang.m_c_animator);
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(lang.header_animator_flags);
                EditorGUILayout.PropertyField(m_anim_onHeld, lang.m_anim_onHeld);
                EditorGUILayout.PropertyField(m_anim_onHeldLocal, lang.m_anim_onHeldLocal);
                EditorGUILayout.PropertyField(m_anim_onTrack, lang.m_anim_onTrack);
                EditorGUILayout.PropertyField(m_anim_onTrackLocal, lang.m_anim_onTrackLocal);
            }

            serializedObject.ApplyModifiedProperties();
        }

    }
}