﻿using HoloToolkit.Unity;
using ROSBridgeLib.actionlib_msgs;
using ROSBridgeLib.art_msgs;
using ROSBridgeLib.diagnostic_msgs;
using ROSBridgeLib.geometry_msgs;
using ROSBridgeLib.std_msgs;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
//using System.Threading;
using UnityEngine;

public class PickFromFeederIP : Singleton<PickFromFeederIP> {

    private InterfaceStateMsg interfaceStateMsg;
    private ProgramItemMsg programItemMsg;
    
    public GameObject pointerToSpawn;

    public GameObject BasicObjectManipulatablePrefab;
    //public GameObject BasicObjectUnmanipulatablePrefab;

    private GameObject cursor;
    private Transform pointedArea;
    private GameObject world_anchor;

    private Vector3 robotArmPosition;
    private Quaternion robotArmRotation;
    private string objectTypeToPick;

    public GameObject robotArm;
    private GameObject objectToPick;
    //private GameObject objectToPickUnmanipulatable;

    private bool waiting_for_action_response = false;
    private string currentRequestID;

    // Use this for initialization
    void Start () {
        cursor = GameObject.FindGameObjectWithTag("cursor");
        world_anchor = GameObject.FindGameObjectWithTag("world_anchor");
    }

    // Update is called once per frame
    void Update() {

    }


    public void StartLearning() {

    }

    public void Visualize() {
        if(ProgramHelper.ItemLearned(programItemMsg)) {
            Debug.Log("Visualizing PICK_FROM_FEEDER");

            //if (objectToPickUnmanipulatable != null)
            //    Destroy(objectToPickUnmanipulatable);

            //convert ros coordinate system to unity coordinate system
            Vector3 robotArmPosition = ROSUnityCoordSystemTransformer.ConvertVector(programItemMsg.GetPose()[0].GetPose().GetPosition().GetPoint());
            Quaternion robotArmRotation = ROSUnityCoordSystemTransformer.ConvertQuaternion(programItemMsg.GetPose()[0].GetPose().GetOrientation().GetQuaternion());

            //show robot arm
            robotArm.transform.localPosition = robotArmPosition;
            robotArm.transform.localRotation = robotArmRotation;
            robotArm.GetComponent<PR2GripperController>().SetArmActive(true);

            Vector3 objectPosition = FakeFeederObjectsPositions.GetObjectPositionInFeeder(programItemMsg.GetObject()[0],
                (robotArmPosition.x > MainMenuManager.Instance.currentSetup.GetTableWidth() / 2f ? FakeFeederObjectsPositions.FeederType.right_feeder : FakeFeederObjectsPositions.FeederType.left_feeder));
            Quaternion objectOrientation = FakeFeederObjectsPositions.GetObjectOrientationInFeeder(programItemMsg.GetObject()[0],
                (robotArmPosition.x > MainMenuManager.Instance.currentSetup.GetTableWidth() / 2f ? FakeFeederObjectsPositions.FeederType.right_feeder : FakeFeederObjectsPositions.FeederType.left_feeder));
            Vector3 objectDims = ObjectsManager.Instance.GetObjectTypeDimensions(programItemMsg.GetObject()[0]);

            //objectToPickUnmanipulatable = Instantiate(BasicObjectUnmanipulatablePrefab, world_anchor.transform);
            //objectToPickUnmanipulatable.transform.localPosition = ROSUnityCoordSystemTransformer.ConvertVector(objectPosition);
            //objectToPickUnmanipulatable.transform.localRotation = ROSUnityCoordSystemTransformer.ConvertQuaternion(objectOrientation);
            //objectToPickUnmanipulatable.transform.GetChild(0).transform.localScale = objectDims;

            if(objectToPick == null) {
                objectToPick = Instantiate(BasicObjectManipulatablePrefab, world_anchor.transform);
            }
            objectToPick.GetComponent<ObjectManipulationEnabler>().DisableManipulation();
            objectToPick.transform.localPosition = ROSUnityCoordSystemTransformer.ConvertVector(objectPosition);
            objectToPick.transform.localRotation = ROSUnityCoordSystemTransformer.ConvertQuaternion(objectOrientation);
            objectToPick.transform.GetChild(0).transform.localScale = objectDims;
        }
    }

    public void VisualizeClear() {
        if (ProgramHelper.ItemLearned(programItemMsg)) {
            robotArm.GetComponent<PR2GripperController>().PlaceGripperToInit();
            robotArm.GetComponent<PR2GripperController>().SetArmActive(false);

            //if (objectToPickUnmanipulatable != null)
            //    Destroy(objectToPickUnmanipulatable);
            if (objectToPick != null) {
                objectToPick.GetComponent<ObjectManipulationEnabler>().EnableManipulation();
                objectToPick.GetComponent<PlaceRotateConfirm>().DestroyItself();
                objectToPick = null;
            }
        }
    }
     
    public void SetInterfaceStateMsgFromROS(InterfaceStateMsg msg) {
        interfaceStateMsg = msg;
        programItemMsg = msg.GetProgramCurrentItem();
    }


    public void MarkClickedArea(DetectedObject detectedObject) {
        //if (objectToPick != null) {
        //    Destroy(objectToPick);
        //}

        pointedArea = cursor.transform;

        //TODO mozna zalezi na strane feederu s tim forward vektorem..
        robotArmPosition = world_anchor.transform.InverseTransformPoint(pointedArea.localPosition + pointedArea.forward * 0.3f);
        robotArmRotation = Quaternion.Inverse(world_anchor.transform.localRotation) * pointedArea.localRotation;

        //rotate gripper to face feeder
        //picking from right feeder
        if(robotArmPosition.x > MainMenuManager.Instance.currentSetup.GetTableWidth()/2) {
            Debug.Log("PICKING FROM RIGHT FEEDER");
            robotArmRotation.eulerAngles += new Vector3(90, 0, 0);
        }
        //picking from left feeder
        else {
            Debug.Log("PICKING FROM LEFT FEEDER");
            robotArmRotation.eulerAngles += new Vector3(-90, 0, 0);
        }


        objectToPick = Instantiate(BasicObjectManipulatablePrefab, detectedObject.position, detectedObject.rotation);
        objectToPick.GetComponent<ObjectManipulationEnabler>().EnableManipulation();
        objectToPick.transform.GetChild(0).transform.localScale = detectedObject.bbox;
        objectToPick.transform.GetChild(0).GetComponent<Collider>().enabled = false;
        objectToPick.transform.parent = cursor.transform;
        objectToPick.transform.localPosition = new Vector3(0, 0, detectedObject.bbox.x/2);
        objectToPick.transform.localEulerAngles = new Vector3(0f, 90f, 0f);
        objectToPick.GetComponent<PlaceRotateConfirm>().Arm = 
            (robotArmPosition.x > MainMenuManager.Instance.currentSetup.GetTableWidth() / 2) ? RobotRadiusHelper.RobotArm.LEFT_ARM : RobotRadiusHelper.RobotArm.RIGHT_ARM;
        
        objectTypeToPick = detectedObject.type;

        StartCoroutine(SaveToROS());
    }

    private IEnumerator SaveToROS() {
        ROSActionHelper.OnLearningActionResult += OnLearningActionResult;

        Debug.Log("PICK FROM FEEDER SAVED");

        //Save parameters for PICK_FROM_FEEDER
        TimeMsg currentTime = ROSTimeHelper.GetCurrentTime();
        InterfaceStateMsg i_msg = new InterfaceStateMsg(
            "HOLOLENS", 
            InterfaceStateMsg.SystemState.STATE_LEARNING,
            currentTime, 
            interfaceStateMsg.GetProgramID(), 
            interfaceStateMsg.GetBlockID(), 
            new ProgramItemMsg(
                programItemMsg.GetID(), 
                programItemMsg.GetOnSuccess(), 
                programItemMsg.GetOnFailure(), 
                programItemMsg.GetIType(), 
                programItemMsg.GetName(), 
                new List<string>() {objectTypeToPick}, 
                new List<PoseStampedMsg>() {
                    new PoseStampedMsg(new HeaderMsg(0, currentTime, "marker"), 
                    new PoseMsg(new PointMsg(ROSUnityCoordSystemTransformer.ConvertVector(robotArmPosition)), 
                                new QuaternionMsg(ROSUnityCoordSystemTransformer.ConvertQuaternion(robotArmRotation)))) },
                new List<PolygonStampedMsg>(),
                programItemMsg.GetRefID(), 
                programItemMsg.GetFlags(), 
                programItemMsg.GetDoNotClear(), 
                programItemMsg.GetLabels()),
            interfaceStateMsg.GetFlags(), 
            true, 
            InterfaceStateMsg.ErrorSeverity.NONE, 
            InterfaceStateMsg.ErrorCode.ERROR_UNKNOWN);
        ROSCommunicationManager.Instance.ros.Publish(InterfaceStatePublisher.GetMessageTopic(), i_msg);

        yield return new WaitForSecondsRealtime(0.1f);

        //Request brain for goal DONE
        currentTime = ROSTimeHelper.GetCurrentTime();
        currentRequestID = ROSActionHelper.GenerateUniqueGoalID((int)learning_request_goal.DONE, ROSTimeHelper.ToSec(currentTime));
        LearningRequestActionGoalMsg requestMsg = new LearningRequestActionGoalMsg(new HeaderMsg(0, currentTime, ""), 
            new GoalIDMsg(ROSTimeHelper.GetCurrentTime(), currentRequestID),
            new LearningRequestGoalMsg(learning_request_goal.DONE));
        ROSCommunicationManager.Instance.ros.Publish(LearningRequestActionGoalPublisher.GetMessageTopic(), requestMsg);
        waiting_for_action_response = true;

        //Wait for action server response
        yield return new WaitWhile(() => waiting_for_action_response == true);
        Debug.Log("SERVER RESPONDED!");
        yield return new WaitForSecondsRealtime(0.1f);

        //Switch to next instruction (should be PLACE_TO_POSE)
        //InteractiveProgrammingManager.Instance.PlaceToPoseLearningOverride = true;
        currentTime = ROSTimeHelper.GetCurrentTime();
        i_msg = new InterfaceStateMsg(
            "HOLOLENS",
            InterfaceStateMsg.SystemState.STATE_LEARNING,
            currentTime,
            interfaceStateMsg.GetProgramID(),
            interfaceStateMsg.GetBlockID(),
            ProgramHelper.GetProgramItemById(interfaceStateMsg.GetBlockID(), programItemMsg.GetOnSuccess()),
            interfaceStateMsg.GetFlags(),
            false,
            InterfaceStateMsg.ErrorSeverity.NONE,
            InterfaceStateMsg.ErrorCode.ERROR_UNKNOWN);
        ROSCommunicationManager.Instance.ros.Publish(InterfaceStatePublisher.GetMessageTopic(), i_msg);

        yield return new WaitForSecondsRealtime(0.1f);

        //Request brain for goal GET_READY for learning PLACE_TO_POSE
        currentTime = ROSTimeHelper.GetCurrentTime();
        currentRequestID = ROSActionHelper.GenerateUniqueGoalID((int)learning_request_goal.GET_READY_WITHOUT_ROBOT, ROSTimeHelper.ToSec(currentTime));
        requestMsg = new LearningRequestActionGoalMsg(new HeaderMsg(0, currentTime, ""), 
            new GoalIDMsg(currentTime, currentRequestID),
            new LearningRequestGoalMsg(learning_request_goal.GET_READY_WITHOUT_ROBOT));
        ROSCommunicationManager.Instance.ros.Publish(LearningRequestActionGoalPublisher.GetMessageTopic(), requestMsg);
        waiting_for_action_response = true;

        //Wait for action server response
        yield return new WaitWhile(() => waiting_for_action_response == true);
        Debug.Log("SERVER RESPONDED!");

        yield return new WaitForSecondsRealtime(0.1f);

        ROSActionHelper.OnLearningActionResult -= OnLearningActionResult;
        yield return null;
    }

    private void OnLearningActionResult(LearningRequestActionResultMsg msg) {
        //if result ID is identical with request ID
        if(msg.GetStatus().GetGoalID().GetID().Equals(currentRequestID) && msg.GetStatus().GetStatus() == GoalStatusMsg.Status.SUCCEEDED
            && msg.GetResult().GetSuccess() == true) {
            waiting_for_action_response = false;
        }
    }
}
