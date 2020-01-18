using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    //movement variables
    Vector3 moveDirection;
    [HideInInspector]public Vector3 currentDirection;
    [HideInInspector]public Vector3 previousDirection;
    [HideInInspector]public float forwardVelocity = 0f;
    Vector3 zDirection;
    Vector3 xDirection;
    float verticalVelocity;

    float forwardInput;
    float rightInput;

    [Header("Movement variables")]
    public float movementSpeed;
    public float rotationSpeed;
    public float gravity;
    public LayerMask levelMask;

    //references
    Animator anim;
    CharacterController charController;
    Camera cam;

    //layer mask for ground
    RaycastHit groundHit;

    //force to spring up with when pelvis below optimal standing level
    public float pelvisSpringForce;

    [Space]
    [SerializeField]private float walkStrideLength = 0;
    [SerializeField]private float runStrideLength = 12;
    public float strideLength;
    public float distanceCovered;

    [Header("Public references")]
    //actual pivot of the model
    public Transform pelvisPosition;
    Ray pelvisForwardRay;
    [SerializeField]private float standingPelvisHeightMax = 1.3f; //1.01
    [SerializeField]private float standingPelvisHeightMin = 1f; //0.95
    public float currentStandingPelvisHeightMax;
    public float currentStandingPelvisHeightMin;

    //crouching variables
    [Space]
    RaycastHit obstacleInfo;

    //access animation script on the graphic element
    public PlayerAnimation animationScript;

    // Start is called before the first frame update
    void Start()
    {
        //cursor
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = false;

        cam = Camera.main;   
        anim = GetComponent<Animator>();
        charController = GetComponent<CharacterController>();
    }

    // Update is called once per frame
    void Update()
    {
        DoLocomotion();
    }

    // float GetPelvisHeightFromGround(){
    //     if(Physics.Raycast(pelvisPosition.position, Vector3.down, out groundHit, Mathf.Infinity, levelMask)){
    //         //Debug.DrawLine(pelvisPosition.position, Vector3.down, Color.green, Time.deltaTime);
    //         print((groundHit.point - pelvisPosition.position).magnitude);
    //         return ((groundHit.point - pelvisPosition.position).magnitude);
    //     }
    //     return -1;
    // }

    float GetPelvisHeightFromGround(){
        if(Physics.Raycast(transform.position, Vector3.down, out groundHit, Mathf.Infinity, levelMask)){
            //Debug.DrawLine(pelvisPosition.position, Vector3.down, Color.green, Time.deltaTime);
            print((groundHit.point - transform.position).magnitude);
            return ((groundHit.point - transform.position).magnitude);
        }
        return -1;
    }

    bool GetObstacleInFront(){
        pelvisForwardRay = new Ray(pelvisPosition.position, pelvisPosition.forward);
        if(Physics.SphereCast(pelvisForwardRay, 0.3f, out obstacleInfo, 1f, levelMask)){
            if(obstacleInfo.transform.tag == "Obstacle"){
                return true;
            }    
        }
        return false;
    }

    void SolveHipPosition(){
        int i =0;
        float heightAtNoObstacle = 0f;
        while(Physics.SphereCast(pelvisForwardRay, 0.3f, out obstacleInfo, 1f, levelMask) && obstacleInfo.transform.tag == "Obstacle"){
            pelvisForwardRay = new Ray(new Vector3(pelvisPosition.position.x, pelvisPosition.position.y - 0.1f, pelvisPosition.position.z), pelvisPosition.forward);
            heightAtNoObstacle -= 0.1f;
            currentStandingPelvisHeightMax = pelvisPosition.position.y + heightAtNoObstacle;
            currentStandingPelvisHeightMin = pelvisPosition.position.y + heightAtNoObstacle;
            //arbitrary counts of recursion
            i++;
            if(i>5){ 
                return;
            }    
        }
    }

    void DoLocomotion(){
        previousDirection = new Vector3(moveDirection.x, 0f, moveDirection.z);
        moveDirection = Vector3.zero;

        rightInput = Input.GetAxis("Vertical");
        forwardInput = Input.GetAxis("Horizontal");

        zDirection = cam.transform.forward;
        xDirection = cam.transform.right;
        zDirection.y = xDirection.y = 0f;
        zDirection.Normalize();
        xDirection.Normalize();
 
        currentDirection = moveDirection = xDirection * forwardInput + zDirection * rightInput; 
        
        //solver for tilt
        Debug.DrawLine(transform.position, transform.position + moveDirection, Color.green, 0);
        Debug.DrawLine(transform.position, transform.position + previousDirection, Color.blue, 0);
               
        moveDirection *= movementSpeed;

        forwardVelocity = Vector3.Dot(moveDirection, transform.forward);
        forwardVelocity = Mathf.Clamp(forwardVelocity, 0.1f, 5f);

        if(moveDirection == Vector3.zero)
            distanceCovered = moveDirection.magnitude;  

        //stride logic
        //purely animation based variables, do not use for physics calculations
        strideLength = Mathf.Lerp(walkStrideLength, runStrideLength, forwardVelocity/ 5f);
        distanceCovered += moveDirection.magnitude/strideLength * Time.deltaTime;
        if(distanceCovered > 1f)
            distanceCovered = 0f;  

        if(moveDirection != Vector3.zero)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(moveDirection), rotationSpeed);

        //find comfort height for hip to walk underneath
        if(GetObstacleInFront()){
            SolveHipPosition();
        }
        else{
            currentStandingPelvisHeightMax = standingPelvisHeightMax;
            currentStandingPelvisHeightMin = standingPelvisHeightMin;
        }
        float pelvisHeightFromGround = GetPelvisHeightFromGround();
        if(pelvisHeightFromGround > currentStandingPelvisHeightMax){  //ideal value 1.01
            verticalVelocity -= gravity * Time.deltaTime;
            verticalVelocity = Mathf.Clamp(verticalVelocity,-Mathf.Infinity, 0f);
            print("gravity");
        }
        else if(pelvisHeightFromGround < currentStandingPelvisHeightMin){  //ideal value 0.95
            verticalVelocity += pelvisSpringForce * Time.deltaTime; //this implementation does not work for no ground underneath
            verticalVelocity = Mathf.Clamp(verticalVelocity, 0f, Mathf.Infinity);
            print("pelvis force");
        }
        else 
        {
            verticalVelocity = 0f;
            print("done nothing");
        }
        moveDirection.y = verticalVelocity;
       
        charController.Move(moveDirection * Time.deltaTime);
    }
}
