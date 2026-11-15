using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Efecto sobre UNA barra. Una opcion puede tener muchos efectos
/// (asi eliges a cuantas barras afecta y cuanto).
/// </summary>
[Serializable]
public class BarEffect
{
    public BarType bar;

    [Tooltip("Positivo = sube (flecha verde). Negativo = baja (flecha roja).")]
    public float amount = 10f;

    [Tooltip("Si esta activo ignora 'bar' y afecta a una barra al azar.")]
    public bool randomBar = false;
}

/// <summary>Un boton de decision: su texto y lo que hace con las barras.</summary>
[Serializable]
public class EventChoice
{
    public string label = "Opcion";
    public List<BarEffect> effects = new List<BarEffect>();
}

/// <summary>Un evento completo. Se guardan en la lista del EventManager.</summary>
[Serializable]
public class GameEvent
{
    public string name = "Nuevo evento";   // solo para identificarlo en el inspector

    [Header("Imagen y texto")]
    public Sprite image;
    [TextArea(3, 8)] public string text = "Escribe aqui el texto del evento...";
    public Color textColor = Color.white;
    [Tooltip("0 = usa el tamano por defecto del EventManager")]
    public int fontSize = 0;
    [Tooltip("Velocidad de escritura. 0 = usa la del EventManager")]
    public float charsPerSecond = 0f;

    [Header("Narracion (suena junto al texto)")]
    public AudioClip sound;
    [Range(0f, 1f)] public float soundVolume = 1f;
    [Tooltip("El texto se escribe al ritmo de la narracion y los botones aparecen cuando termina")]
    public bool syncTypingToNarration = true;

    [Header("Decisiones")]
    public EventChoice choiceA = new EventChoice();
    public EventChoice choiceB = new EventChoice();
}
