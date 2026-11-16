using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ChangeSprite : MonoBehaviour
{
    [SerializeField] private Sprite spriteOG, sprite2;
    // Start is called before the first frame update
    
    public void SetOriginalSprite()
    {
        GetComponent<SpriteRenderer>().sprite = spriteOG;
    }

    public void SetSecondSprite()
    {
        GetComponent<SpriteRenderer>().sprite = sprite2;
    }
}

