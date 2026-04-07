using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    public InputField inputField;
    [FormerlySerializedAs("cloth")] public MassSpringCloth massSpringCloth;
    // Start is called before the first frame update
    void Start()
    {
        inputField.onEndEdit.AddListener((value) =>
        {
            print(inputField.name + "的值为" + value);
            massSpringCloth.ChangeSpringKs(Convert.ToSingle(value));
        });
    }


}
