using UnityEngine;

public class LogInstantiation : MonoBehaviour
{
    private void Awake()
    {
        print($"Just got instantiated! {transform.position} , {transform.rotation}, {transform.localScale}");
    }
}
