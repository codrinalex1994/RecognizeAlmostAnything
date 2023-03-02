using UnityEngine;
using UnityEngine.UI;

public class SetImage : MonoBehaviour
{
    [SerializeField] private Image image;

    public void SetPhoto(Texture2D texture2D)
    {
        image.sprite = Sprite.Create(texture2D, new Rect(0, 0, texture2D.width, texture2D.height), 0.5f * Vector2.one);
    }
}
