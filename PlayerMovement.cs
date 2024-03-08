using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    private float moveSpeed;
    public float walkSpeed;
    public float sprintSpeed;
    public float slideSpeed;

    private float desiredMoveSpeed;
    private float lastDesiredMoveSpeed;

    public float wallrunSpeed;

    public float speedIncreaseMultiplier;
    public float slopeIncreaseMultiplier;

    public float groundDrag;

    [Header("Jumping")]
    public float jumpForce;
    public float jumpCooldown;
    public float airMultiplier;
    bool readyToJump;

    [Header("Crouching")]
    public float crouchSpeed;
    public float crouchYScale;
    private float startYScale;

    [Header("Keybinds")]
    public KeyCode jumpKey = KeyCode.Space;
    public KeyCode sprintKey = KeyCode.LeftShift;
    public KeyCode crouchKey = KeyCode.LeftControl;

    [Header("Ground Check")]
    public float playerHeight;
    public LayerMask whatIsGround;
    bool grounded;

    [Header("Slope Handling")]
    public float maxSlopeAngle;
    private RaycastHit slopeHit;
    private bool exitingSlope;

    public Transform orientation;

    float horizontalInput;
    float verticalInput;

    Vector3 moveDirection;

    Rigidbody rb;

    public MovementState state;

    public enum MovementState
    {
        walking,
        sprinting,
        wallrunning,
        crouching,
        sliding,
        air
    }

    public bool sliding;
    public bool wallrunning;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;

        readyToJump = true;

        startYScale = transform.localScale.y;
    }

    private void Update()
    {
        //ground check
        grounded = Physics.Raycast(transform.position, Vector3.down, playerHeight * 0.5f + 0.2f, whatIsGround);

        MyInput();
        SpeedControl();
        StateHandler();

        //GuiText();

        //handle drag
        if (grounded)
            rb.drag = groundDrag;
        else
            rb.drag = 0;
    }


    private void FixedUpdate()
    {
        MovePlayer();
    }


    private void MyInput()
    {
        horizontalInput = Input.GetAxisRaw("Horizontal");
        verticalInput = Input.GetAxisRaw("Vertical");

        //when jump is activated
        if(Input.GetKey(jumpKey) && readyToJump && grounded)
        {
            readyToJump = false;

            Jump();

            //for autobunnyhopping
            Invoke(nameof(ResetJump), jumpCooldown);
        }    

        //start crouch
        if(Input.GetKeyDown(crouchKey))
        {
            //shrink the player when crouched
            transform.localScale = new Vector3(transform.localScale.x, crouchYScale, transform.localScale.z);

            //fix issue where shrunk player will be floating - add small downwards force to push player down
            rb.AddForce(Vector3.down * 5f, ForceMode.Impulse);
        }    

        //stop crouch
        if (Input.GetKeyUp(crouchKey))
        {
            transform.localScale = new Vector3(transform.localScale.x, startYScale, transform.localScale.z);
        }
    }

    private void StateHandler()
    {
        // WALLRUNNING
        if (wallrunning)
        {
            state = MovementState.wallrunning;
            desiredMoveSpeed = wallrunSpeed;
        }


        // SLIDING
        if (sliding)
        {
            state = MovementState.sliding;

            if (OnSlope() && rb.velocity.y < 0.1f)
                desiredMoveSpeed = slideSpeed;

            else
                desiredMoveSpeed = sprintSpeed;
        }

        // CROUCHING
        else if (Input.GetKey(crouchKey))
        {
            state = MovementState.crouching;
            desiredMoveSpeed = crouchSpeed;
        }

        // SPRINTING
        else if(grounded && Input.GetKey(sprintKey))
        {
            state = MovementState.sprinting;
            desiredMoveSpeed = sprintSpeed;
        }

        // WALKING
        else if (grounded)
        {
            state = MovementState.walking;
            desiredMoveSpeed = walkSpeed;
        }

        // AIR
        else
        {
            state = MovementState.air;
        }

        // check if desiredMoveSpeed has changed significantly
        if (Mathf.Abs(desiredMoveSpeed - lastDesiredMoveSpeed) > 4f && moveSpeed != 0)
        {
            StopAllCoroutines();
            StartCoroutine(SmoothlyLerpMoveSpeed());
        }
        else
        {
            moveSpeed = desiredMoveSpeed;
        }

        lastDesiredMoveSpeed = desiredMoveSpeed; 
    }

    private IEnumerator SmoothlyLerpMoveSpeed()
    {
        //smoothly lerp moveSpeed to desired value
        float time = 0;
        float difference = Mathf.Abs(desiredMoveSpeed - moveSpeed);
        float startValue = moveSpeed;

        while (time < difference)
        {
            moveSpeed = Mathf.Lerp(startValue, desiredMoveSpeed, time / difference);

            if (OnSlope())
            {
                float slopeAngle = Vector3.Angle(Vector3.up, slopeHit.normal);
                float slopeAngleIncrease = 1 + (slopeAngle / 90f);

                time += Time.deltaTime * speedIncreaseMultiplier * slopeIncreaseMultiplier * slopeAngleIncrease;
            }
            else
                time += Time.deltaTime * speedIncreaseMultiplier;

            yield return null;
        }

        moveSpeed = desiredMoveSpeed;
    }


    private void MovePlayer()
    {
        //calculate the movement direction

        //always move in the direction you are looking
        moveDirection = orientation.forward * verticalInput + orientation.right * horizontalInput;

        //slope
        if(OnSlope() && !exitingSlope)
        {
            rb.AddForce(GetSlopeMoveDirection(moveDirection) * moveSpeed * 20f, ForceMode.Force);

            //if player moving upwards
            if (rb.velocity.y > 0)
                //add downwards force to keep player on slope
                rb.AddForce(Vector3.down * 80f, ForceMode.Force);
        }

        //ground
        else if(grounded)
            rb.AddForce(moveDirection.normalized * moveSpeed * 10f, ForceMode.Force);

        //air
        else if(!grounded)                                           /////////////
            rb.AddForce(moveDirection.normalized * moveSpeed * 10f * airMultiplier, ForceMode.Force);
                                                                     /////////////

        //disable gravity when on slope
        rb.useGravity = !OnSlope();
    }

    //limit players speed manually
    private void SpeedControl()
    {
        //limit speed on slope
        if (OnSlope() && !exitingSlope)
        {
            //limit velocity to movespeed no matter the direction
            if (rb.velocity.magnitude > moveSpeed)
                rb.velocity = rb.velocity.normalized * moveSpeed;
        }

        else
        {
            //flat velocity of rigidbody
            Vector3 flatVelocity = new Vector3(rb.velocity.x, 0f, rb.velocity.z);

            //check if need to limit the velocity
            if (flatVelocity.magnitude > moveSpeed)         //if going faster than movespeed
            {
                Vector3 limitedVelocity = flatVelocity.normalized * moveSpeed;  //calculate what maxvel would be
                rb.velocity = new Vector3(limitedVelocity.x, rb.velocity.y, limitedVelocity.z); //apply it
            }
            //text_speed.SetText("Speed: " + Round(flatVelocity.magnitude, 1));

            //text_mode.SetText(state.ToString());
        }
    }

    private void Jump()
    {
        exitingSlope = true;

        //reset Y vel
        rb.velocity = new Vector3(rb.velocity.x, 0f, rb.velocity.z);

        //apply force upwards * jumpforce. impulse to apply force once only
        rb.AddForce(transform.up * jumpForce, ForceMode.Impulse);
    }

    private void ResetJump()
    {
        readyToJump = true;
        
        exitingSlope = false;
    }

    public bool OnSlope()
    {
        if(Physics.Raycast(transform.position, Vector3.down, out slopeHit, playerHeight * 0.5f + 0.3f))
        {
            //calculate how steep the angle we are standing on is
            float angle = Vector3.Angle(Vector3.up, slopeHit.normal);
            //return true if not flat / too steep
            return angle < maxSlopeAngle && angle != 0;
        }
        //if raycast misses
        return false;
    }

    public Vector3 GetSlopeMoveDirection(Vector3 direction)
    {
        //project normal move direction onto slope
        return Vector3.ProjectOnPlane(direction, slopeHit.normal).normalized;
    }
    //GUI stuff

    //public TextMeshProUGUI text_speed;
    //public TextMeshProUGUI text_mode;

    //public MovementState state;
    //public enum MovementState
    //{
    //    walking,
    //    sprinting,
    //    wallrunning,
    //    crouching,
    //    sliding,
    //    air
    //}
    //private void GuiText()
    //{
    //    Vector3 flatVelocity = new Vector3(rb.velocity.x, 0f, rb.velocity.z);

    //    text_speed.SetText("Speed: " + Round(flatVelocity.magnitude, 1));

    //    text_mode.SetText(state.ToString());
    //}

    //public static float Round(float value, int digits)
    //{
    //    float mult = Mathf.Pow(10.0f, (float)digits);
    //    return Mathf.Round(value * mult) / mult;
    //}
}
