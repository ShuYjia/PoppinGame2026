using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    public PlayerInputControl inputControl;
    public Vector2 inputDirection;
    public float speed;
    public Rigidbody2D rb;
   // public float hisDir=2;
    // Start is called before the first frame update
    private void Awake()
    {
        inputControl = new PlayerInputControl();
        rb = GetComponent<Rigidbody2D>();
    }
    private void OnEnable()
    {
        inputControl.Enable();
    }
    private void OnDisable()
    {
        inputControl.Disable();
    }
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        inputDirection = inputControl.GamePlay.Move.ReadValue<Vector2>();
        Move();
    }
    private void FixedUpdate()
    {

    }
    public void Move()
    {
        rb.velocity = new Vector2(inputDirection.x * speed * Time.deltaTime, rb.velocity.y);
        //ÈËÎï·­×ª

            float xScale = transform.localScale.x;
            if (inputDirection.x > 0)
                xScale = Mathf.Abs(transform.localScale.x);
            if (inputDirection.x < 0)
                xScale = -Mathf.Abs(transform.localScale.x);
            transform.localScale = new Vector3(xScale, transform.localScale.y, transform.localScale.z);
        //    hisDir = xScale;

    }
}
