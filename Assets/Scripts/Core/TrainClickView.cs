using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TrainClickView : MonoBehaviour
{
    Action trainClicked;

    internal void Init(Action trainWasClicked)
    {
        trainClicked = trainWasClicked;
    }

    internal void OnClickedByRaycast()
    {
        trainClicked?.Invoke();
    }

    
}
