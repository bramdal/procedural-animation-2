using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor.Animations;

public class PlayerAnimation : MonoBehaviour
{

    public float tiltRate;

    Animator anim;
    public PlayerMovement movementScript;

    int strideCount = 2;

    
    private Vector3 rightFootPosition, leftFootPosition;
    private Vector3 rightFootIKPosition, leftFootIKPosition;
    private Quaternion rightFootIKRotation, leftFootIKRotation;
    private float lastPelvisPositionY, lastRightFootPositionY, lastLeftFootPositionY;

    [Header("Feet Inverse Kinematics Variables")]
    public bool enableFeetIK = true;
    public bool enablePelvisShift = false;
    public bool enableAccelerationTilt = true;
    [SerializeField] private float heightFromGroundRaycast = 1.5f;
    [SerializeField] private float raycastDownDistance = 1.5f;
    [SerializeField] private LayerMask levelLayer;
    [SerializeField] private float pelvisOffset = 0f;
    [SerializeField] private float pelvisUpAndDownSpeed = 0.3f;
    [SerializeField] private float feetToIKPositionSpeed = 0.5f;

    public string rightFootAnimVariableName = "RightFootCurve";
    public string leftFootAnimVariableName = "LeftFootCurve";

    public bool useFeetRotation;
    public bool showSolverDebug;

    //head ik
    public Transform head;
    Ray headForwardRay;
    RaycastHit obstacleInfo;
    Vector3 headSolvedPosition;
    Vector3 chestSolvedPosition;
    float slerpTime;
    bool crouching = false;
    [Range(0,1)]
    public float slerpStart;

    // Start is called before the first frame update
    void Start()
    {
        anim = GetComponent<Animator>();
    }

    private void LateUpdate(){

        slerpTime += Time.deltaTime * 5;

        if(movementScript.currentDirection != Vector3.zero){
            anim.SetBool(("Locomotion"), true);
            anim.SetFloat("Velocity", movementScript.forwardVelocity);
        }
        else{
            anim.SetBool(("Locomotion"), false);
            anim.SetFloat("Velocity", 0f);
        }

        //for state machince
        // if(movementScript.distanceCovered > movementScript.strideLength /2){
        //     StepAnimation(movementScript.distanceCovered, movementScript.strideLength);
        // }

        //for blend tree
        InterpolateStride(movementScript.distanceCovered, movementScript.strideLength);
    }

    private void FixedUpdate() {
        //FeetGrounding
        if(enableFeetIK == false){return;} //do nothing if feature disabled
        if(anim == null){return;}   //do nothing if animator not found

        AdjustFeetTarget(ref rightFootPosition, HumanBodyBones.RightFoot); //Adjust feet IK for right foot
        AdjustFeetTarget(ref leftFootPosition, HumanBodyBones.LeftFoot);  //Adjust feet IK for left foot

        //shoot raycast and solve position of IK
        SolveFeetPositon(rightFootPosition, ref rightFootIKPosition, ref rightFootIKRotation); 
        SolveFeetPositon(leftFootPosition, ref leftFootIKPosition, ref leftFootIKRotation); 

        //solve head collission
        if(GetObstacleInFrontOfHead()){
            SolveHeadPosition();
        }
        
        if(!crouching){
            headSolvedPosition = chestSolvedPosition = Vector3.zero;
        }
    }

    private void OnAnimatorIK(int layerIndex){
        if(enableAccelerationTilt && movementScript.currentDirection!= Vector3.zero) 
            AccelerationTilt(movementScript.currentDirection, movementScript.previousDirection);
       
        //feet ik
        if(enableFeetIK == false){return;}
        if(anim == null){return;}

        if(enablePelvisShift)
            MovePelvisHeight(); //tilt and shift pelvis to match legs

        anim.SetIKPositionWeight(AvatarIKGoal.RightFoot, 1f);  //blend weights for IK are 1 by default
        if(useFeetRotation){
            anim.SetIKRotationWeight(AvatarIKGoal.RightFoot, anim.GetFloat(rightFootAnimVariableName));
        }

        MoveFeetToIKPoint(AvatarIKGoal.RightFoot, rightFootIKPosition, rightFootIKRotation, ref lastRightFootPositionY);

        anim.SetIKPositionWeight(AvatarIKGoal.LeftFoot, 1f);
        if(useFeetRotation){
            anim.SetIKRotationWeight(AvatarIKGoal.LeftFoot, anim.GetFloat(leftFootAnimVariableName));
        }

        MoveFeetToIKPoint(AvatarIKGoal.LeftFoot, leftFootIKPosition, leftFootIKRotation, ref lastLeftFootPositionY); 

        //head ik
        MoveHeadToIKPoint();
        MoveChestToIKPoint();
    }

    #region accelertion tilt and stride
    private void AccelerationTilt(Vector3 currentDirection, Vector3 newDirection){
        if(movementScript.forwardVelocity > 2.5){
            if(Vector3.Angle(currentDirection, newDirection) != 0f){
                anim.SetBoneLocalRotation(HumanBodyBones.Hips, Quaternion.Slerp(transform.localRotation, Quaternion.FromToRotation(transform.forward, newDirection) * Quaternion.FromToRotation(transform.forward, transform.up), tiltRate));
                //note -> quaternion * quaternion is a non commutative
                //interpolate between current rotation and rotation represented by change in direction after rotating that representation of rotation by 90 degrees to face up (since tilting is changing up direction towards change)
            } 
        }
    }

    private void StepAnimation(float distanceCovered, float strideLength){  
        movementScript.distanceCovered = 0f;
        anim.SetTrigger("step");
        strideCount--;
        if(strideCount==0){
            bool currentMirror = anim.GetBool("mirror");
            anim.SetBool("mirror", !currentMirror);
            strideCount = 2;
        }
    }

    private void InterpolateStride(float distanceCovered, float strideLength){
        if(movementScript.distanceCovered >= 1f)
            movementScript.distanceCovered = 0f;
        anim.SetFloat("DistanceCovered", distanceCovered);
    }
    #endregion

    #region FeetIK
    //FeetGroundingMethods

    void MoveFeetToIKPoint(AvatarIKGoal foot, Vector3 positionIKHolder, Quaternion rotationIKHolder, ref float lastFootPositionY){
        Vector3 targetIKPosition = anim.GetIKPosition(foot);
        
        if(positionIKHolder != Vector3.zero){
            targetIKPosition = transform.InverseTransformPoint(targetIKPosition);
            positionIKHolder = transform.InverseTransformPoint(positionIKHolder);

            float lastFootHeight = Mathf.Lerp(lastFootPositionY, positionIKHolder.y, feetToIKPositionSpeed); //store height at which animation places foot bone
            targetIKPosition.y += lastFootHeight; //adjust for change in height

            lastFootPositionY = lastFootHeight; //feed back temp value after changes

            targetIKPosition = transform.TransformPoint(targetIKPosition); //convert to world space
            anim.SetIKRotation(foot, rotationIKHolder); //give new foot rotation to anim system
        }
        
        anim.SetIKPosition(foot, targetIKPosition); //give new foot position to anim system
    }

    private void MovePelvisHeight(){
        if(rightFootIKPosition == Vector3.zero || leftFootIKPosition == Vector3.zero || lastPelvisPositionY == 0){
            lastPelvisPositionY = anim.bodyPosition.y;
            return;
        }

        float lOffsetPosition = leftFootIKPosition.y - transform.position.y;
        float rOffsetPosition = rightFootIKPosition.y - transform.position.y;

        float totalOffest = (lOffsetPosition < rOffsetPosition) ? lOffsetPosition : rOffsetPosition;

        Vector3 newPelvisPosition = anim.bodyPosition + Vector3.up * totalOffest;

        newPelvisPosition.y = Mathf.Lerp(lastPelvisPositionY, newPelvisPosition.y, pelvisUpAndDownSpeed);

        anim.bodyPosition = newPelvisPosition;
        lastPelvisPositionY = anim.bodyPosition.y;
    }

    //locate feet position using raycasts to solve new position for IK
    private void SolveFeetPositon(Vector3 fromSkyPosition, ref Vector3 feetIKPosition, ref Quaternion feetIKRotations){
        RaycastHit feetOutHit;

        if(showSolverDebug)
            Debug.DrawLine(fromSkyPosition, fromSkyPosition + Vector3.down * (raycastDownDistance + heightFromGroundRaycast), Color.black); 

        if(Physics.Raycast(fromSkyPosition, Vector3.down, out feetOutHit, raycastDownDistance + heightFromGroundRaycast, levelLayer)){
            feetIKPosition = fromSkyPosition;
            feetIKPosition.y = feetOutHit.point.y + pelvisOffset;
            feetIKRotations = Quaternion.FromToRotation(Vector3.up, feetOutHit.normal) * transform.rotation;
            return;
        }
        feetIKPosition = Vector3.zero; // return feet to local origin if no ground found
    }

    private void AdjustFeetTarget(ref Vector3 feetPositions, HumanBodyBones foot){
        feetPositions = anim.GetBoneTransform(foot).position;
        feetPositions.y = transform.position.y + heightFromGroundRaycast;
    }
    #endregion

   bool GetObstacleInFrontOfHead(){
        //headSolvedPosition = Vector3.zero;
        headForwardRay = new Ray(head.position, transform.forward);
        if(Physics.SphereCast(headForwardRay, 0.15f, out obstacleInfo, 2f, levelLayer)){
            if(obstacleInfo.transform.tag == "Obstacle"){
                return true;
            }    
        }
        return false;
    }

    void SolveHeadPosition(){
        chestSolvedPosition = Vector3.zero;
        slerpTime =slerpStart;
        int i =0;
        float heightAtNoObstacle = 0f;
        while(Physics.SphereCast(headForwardRay, 0.15f, out obstacleInfo, 2f, levelLayer) && obstacleInfo.transform.tag == "Obstacle"){
            headForwardRay = new Ray(new Vector3(head.position.x, head.position.y - 0.05f, head.position.z), transform.forward);
            heightAtNoObstacle -= 0.05f;
            //headSolvedPosition = obstacleInfo.point - head.position;
            headSolvedPosition = new Vector3(0f, head.position.y - heightAtNoObstacle, 0f);
            headSolvedPosition.x = headSolvedPosition.z = 0f;
            //Debug.DrawLine(head.position, head.position - headSolvedPosition, Color.green, 0);
            //arbitrary counts of recursion
            i++;
            if(i>3){ 
                SolveChestPosition();
                return;
            }    
        }
    }

    void SolveChestPosition(){
        headSolvedPosition = Vector3.zero;
        slerpTime = slerpStart;
        int i =0;
        float heightAtNoObstacle = 0f;
        while(Physics.SphereCast(headForwardRay, 0.15f, out obstacleInfo, 2f, levelLayer) && obstacleInfo.transform.tag == "Obstacle"){
            headForwardRay = new Ray(new Vector3(head.position.x, head.position.y - 0.05f, head.position.z), transform.forward);
            heightAtNoObstacle -= 0.05f;
            //headSolvedPosition = obstacleInfo.point - head.position;
            chestSolvedPosition = new Vector3(0f, head.position.y - heightAtNoObstacle, 0f);
            headSolvedPosition.x = headSolvedPosition.z = 0f;
            //Debug.DrawLine(head.position, head.position - headSolvedPosition, Color.green, 0);
            //arbitrary counts of recursion
            i++;
            if(i>2){ 
                //chestSolvedPosition = Vector3.zero;
                return;
            }    
        }
    }

    void MoveHeadToIKPoint(){
        Quaternion currentRotation = Quaternion.Slerp(anim.GetBoneTransform(HumanBodyBones.Neck).localRotation, Quaternion.FromToRotation(headSolvedPosition, transform.forward), slerpTime);
        anim.SetBoneLocalRotation(HumanBodyBones.Neck, currentRotation);
    }

    void MoveChestToIKPoint(){
        Quaternion currentRotation = Quaternion.Slerp(anim.GetBoneTransform(HumanBodyBones.UpperChest).localRotation, Quaternion.FromToRotation(chestSolvedPosition, transform.forward), slerpTime);
        anim.SetBoneLocalRotation(HumanBodyBones.UpperChest, currentRotation);
    }

    private void OnTriggerEnter(Collider other) {
        if(other.tag == "Obstacle"){
            print("crouch");
            crouching = true;
        }
    }

    private void OnTriggerExit(Collider other) {
        if(other.tag == "Obstacle"){
            print("get up");
            crouching = false;
        }
    }

}
