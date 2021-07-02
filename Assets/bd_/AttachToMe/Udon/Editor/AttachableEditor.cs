using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UdonSharpEditor;

namespace net.fushizen.attachable
{
    [CustomEditor(typeof(AttachableConfig))]
    [CanEditMultipleObjects]
    class AttachableEditor : Editor
    {
        static bool lang_jp = false;
        bool show_internal = false;
        bool show_general = true;
        bool show_selection = true;
        bool show_perms = true;

        SerializedProperty m_t_pickup;
        SerializedProperty m_t_attachmentDirection;
        SerializedProperty m_t_support;

        SerializedProperty m_range;
        SerializedProperty m_directionality;
        SerializedProperty m_preferSelf;

        SerializedProperty m_perm_removeTracee;
        SerializedProperty m_perm_removeOwner;
        SerializedProperty m_perm_removeOther;

        struct Strings
        {
            public GUIContent btn_langSwitch;

            public GUIContent header_general;

            public GUIContent m_t_pickup;

            public GUIContent header_selection;
            public GUIContent m_range;
            public GUIContent m_preferSelf;

            public GUIContent m_t_attachmentDirection;
            public GUIContent m_directionality;

            public GUIContent header_pickup_perms;
            public GUIContent header_pickup_perms_2;
            public GUIContent header_pickup_perms_3;

            public GUIContent m_perm_removeTracee;
            public GUIContent m_perm_removeOwner;
            public GUIContent m_perm_removeOther;

            public GUIContent header_internal;

            public GUIContent m_t_support;

            public GUIContent warn_direction, warn_missing;
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
                m_range = new GUIContent("プレイヤー選択範囲", "この範囲内のプレイヤーが選択対象になります"),
                m_preferSelf = new GUIContent("自分を優先", "チェックが入った場合、自分自身を最初の選択候補として設定します。入ってない場合は自分が最後になります。"),
                m_directionality = new GUIContent("指向性", "この数値をゼロ以上にすると、【方向マーカー】に指定されたオブジェクトのZ+方向の先にあるボーンを優先的に選択します。"),
                m_t_attachmentDirection = new GUIContent("方向マーカー", "このオブジェクトのZ+方向と位置を基準にボーンの選択を行います"),
                warn_direction = new GUIContent("方向マーカーをプレハブの子に設定してください"),
                header_pickup_perms = new GUIContent("取り外しできるプレイヤーの設定"),
                header_pickup_perms_2 = new GUIContent("追尾しているピックアップを取り外せるプレイヤーを設定できます。"),
                header_pickup_perms_3 = new GUIContent("追尾していない場合はだれでも手にとれます。"),
                m_perm_removeTracee = new GUIContent("追尾対象プレイヤー自身"),
                m_perm_removeOwner = new GUIContent("最後に触った人"),
                m_perm_removeOther = new GUIContent("その他の人"),
                header_internal = new GUIContent("内部設定"),
                m_t_support = new GUIContent("サポートプレハブ"),
                warn_missing = new GUIContent("必須項目です。"),
            };

            en = new Strings()
            {
                btn_langSwitch = new GUIContent("日本語に切り替え", "Switch language"),
                header_general = new GUIContent("General settings"),
                m_t_pickup = new GUIContent("Pickup object"),
                header_selection = new GUIContent("Selection configuration"),
                m_range = new GUIContent("Player selection radius", "Players within this radius will be considered as potential targets"),
                m_preferSelf = new GUIContent("Prefer self", "If selected, the player holding the pickup will be the first candidate player. If not, they will be the last."),
                m_directionality = new GUIContent("Directionality", "If you set this value above zero, "),
                m_t_attachmentDirection = new GUIContent("Direction marker", "This object's Z+ direction and position will be used as the basis for bone selection."),
                warn_direction = new GUIContent("Please ensure that the direction marker is a child of the pickup object."),
                header_pickup_perms = new GUIContent("Removal permissions"),
                header_pickup_perms_2 = new GUIContent("Select which players can remove a tracking pickup."),
                header_pickup_perms_3 = new GUIContent("When not tracking a bone, anyone can pick up the pickup."),
                m_perm_removeTracee = new GUIContent("Person being tracked can remove"),
                m_perm_removeOwner = new GUIContent("Last person to touch pickup can remove"),
                m_perm_removeOther = new GUIContent("Anyone else can remove"),
                header_internal = new GUIContent("Internal settings"),
                m_t_support = new GUIContent("Support prefab"),
                warn_missing = new GUIContent("This field is required"),
            };
        }

        private void OnEnable()
        {
            LoadStrings();

            m_t_pickup = serializedObject.FindProperty("t_pickup");
            m_t_attachmentDirection = serializedObject.FindProperty("t_attachmentDirection");
            m_t_support = serializedObject.FindProperty("t_support");

            m_range = serializedObject.FindProperty("range");
            m_directionality = serializedObject.FindProperty("directionality");
            m_preferSelf = serializedObject.FindProperty("preferSelf");

            m_perm_removeTracee = serializedObject.FindProperty("perm_removeTracee");
            m_perm_removeOwner = serializedObject.FindProperty("perm_removeOwner");
            m_perm_removeOther = serializedObject.FindProperty("perm_removeOther");
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
            if (!isMultiple)
            {

                EditorGUILayout.Space();
                show_general = EditorGUILayout.Foldout(show_general, lang.header_general);
                if (show_general)
                {
                    EditorGUILayout.PropertyField(m_t_pickup, lang.m_t_pickup);
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

                EditorGUILayout.Space();
            }

            show_selection = EditorGUILayout.Foldout(show_selection, lang.header_selection);
            if (show_selection)
            {
                EditorGUILayout.PropertyField(m_range, lang.m_range);
                EditorGUILayout.PropertyField(m_preferSelf, lang.m_preferSelf);
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
                }

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

            if (show_selection)
            {
                EditorGUILayout.PropertyField(m_directionality, lang.m_directionality);
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

            if (!isMultiple)
            {
                EditorGUILayout.Space();
                show_internal = EditorGUILayout.Foldout(show_internal, lang.header_internal);
                if (show_internal)
                {
                    EditorGUILayout.PropertyField(m_t_support, lang.m_t_support);
                }
                if (m_t_support.objectReferenceValue == null)
                {
                    if (!show_internal)
                    {
                        show_internal = true;
                        EditorUtility.SetDirty(target);
                    }
                    EditorGUILayout.HelpBox(lang.warn_missing.text, MessageType.Error);
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

    }
}