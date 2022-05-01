# Upgrade guide

## 1.1.x → 1.2

To upgrade from 1.1 to 1.2, simply import the 1.2 unitypackage over the 1.1 package.

**1.2 now supports the UdonSharp 1.0 beta!**
* It is strongly recommended to upgrade to Attach-To-Me 1.2 before upgrading to U#1.0. If you did not do so, please delete the `Attachables Controller` 
object from the scene once manually to avoid issues. 

## 1.1 → 1.1.1

To upgrade from 1.1 to 1.1.1, simply import the 1.1.1 unitypackage over the 1.1 package.

### Major changes in 1.1.1

* Workarounds for various Udon bugs.

## 1.0 → 1.1.1

To upgrade from 1.0 to 1.1 (or 1.1.1):

* First, ensure that you update [UdonSharp](https://github.com/MerlinVR/UdonSharp/releases) to 0.20.2 or higher.
* Import the 1.1.1 unitypackage over the existing 1.0 package. Your scenes should upgrade automatically.

### Major changes in 1.1

* Oculus Quest support! Note, however, that due to udon overhead, it's recommended to avoid having more than about 10 props tracking at a time (as measured on a Quest 1).
* Behavior/stability when making attachable props prefabs has been improved.
* To reduce overhead when many props are tracking, object sync is no longer used to sync the position of props held in hand.
* Directionality now favors the positive Z direction from the directionality marker, instead of both positive and negative Z directions.
* The bone selection interface now considers bones from multiple nearby players, making selection more intuitive.
* The "Prefer self" option has been removed (it's much less necessary now that bone selection considers multiple players).
* Props now find the global controller automatically on scene load; if you delete and recreate the controller, you don't need to mess with each individual prop to fix things.
