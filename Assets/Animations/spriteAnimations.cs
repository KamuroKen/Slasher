using System;
using UnityEditor.Animations;
using UnityEngine;

public class spriteAnimations : MonoBehaviour
{
    private Animator animate;
    public enum selectorBoxType
    {
        light,
        dark,
        lightFlat, 
        darkFlat, 
        darkerFlat,
        other
    }
    public selectorBoxType type;
    public bool isSwitch;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        animate = GetComponent<Animator>();
        ToggleOff();
        
        if (type == selectorBoxType.light)
        {
            animate.Play("selectorBox");
        }
        else if (type == selectorBoxType.dark)
        {
            animate.Play("selectorBox 1");
        }
        else if (type == selectorBoxType.lightFlat)
        {
            animate.Play("selectorBoxLightFLat");
        }
        else if (type == selectorBoxType.darkFlat)
        {
            animate.Play("selectorBoxDarkFlat");
        }
        else if (type == selectorBoxType.darkerFlat)
        {
            animate.Play("selectorBoxDarkerFlat");
        }
    }

    public void ToggleOn()
    {
        if (isSwitch)
        {
            animate.Play("sliderToggleOn");
        }
    }

    public void ToggleOff()
    {
        if (isSwitch)
        {
            animate.Play("sliderToggleOff");
        }
    }
}
