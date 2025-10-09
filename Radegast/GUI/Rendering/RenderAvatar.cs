/**
 * Radegast Metaverse Client
 * Copyright(c) 2009-2014, Radegast Development Team
 * Copyright(c) 2016-2022, Sjofn, LLC
 * All rights reserved.
 *  
 * Radegast is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU Lesser General Public License
 * along with this program.If not, see<https://www.gnu.org/licenses/>.
 */

using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml;
using OpenMetaverse;
using OpenMetaverse.Rendering;
using Path = System.IO.Path;

namespace Radegast.Rendering
{
    public class attachment_point
    {
        public string name;
        public string joint;
        public string location;
        public Vector3 position;
        public Quaternion rotation;
        public int id;
        public int group;

        public GLMesh jointmesh;
        public int jointmeshindex;

        public attachment_point(XmlNode node)
        {
            if (node.Attributes == null) { return; }

            name = node.Attributes.GetNamedItem("name").Value;
            joint = node.Attributes.GetNamedItem("joint").Value;
            location = node.Attributes.GetNamedItem("location").Value;
            position = VisualParamEx.XmlParseVector(node.Attributes.GetNamedItem("position").Value);
            rotation = VisualParamEx.XmlParseRotation(node.Attributes.GetNamedItem("rotation").Value);
            id = Int32.Parse(node.Attributes.GetNamedItem("id").Value);
            @group = Int32.Parse(node.Attributes.GetNamedItem("group").Value);
        }

    }

    /// <summary>
    /// Subclass of LindenMesh that adds vertex, index, and texture coordinate
    /// arrays suitable for pushing direct to OpenGL
    /// </summary>
    public class GLMesh : LindenMesh
    {
        /// <summary>
        /// Subclass of LODMesh that adds an index array suitable for pushing
        /// direct to OpenGL
        /// </summary>
        /// 

        public int teFaceID;
        public Dictionary<int, VisualParamEx> _evp = new Dictionary<int, VisualParamEx>();

        public new class LODMesh : ReferenceMesh
        {
            public ushort[] Indices;

            public override void LoadMesh(string filename)
            {
                base.LoadMesh(filename);

                // Generate the index array
                Indices = new ushort[NumFaces * 3];
                int current = 0;
                for (int i = 0; i < NumFaces; i++)
                {
                    Indices[current++] = (ushort)Faces[i].Indices[0];
                    Indices[current++] = (ushort)Faces[i].Indices[1];
                    Indices[current++] = (ushort)Faces[i].Indices[2];
                }
            }
        }

        public struct GLData
        {
            public float[] Vertices;
            public float[] Normals;
            public ushort[] Indices;
            public float[] TexCoords;
            public Vector3 Center;
            public float[] weights; //strictly these are constant and don't need instancing with the GLMesh
            public string[] skinJoints;  //strictly these are constant and don't need instancing with the GLMesh
        }

        public static GLData baseRenderData;
        public GLData RenderData;
        public GLData OrigRenderData;
        public GLData MorphRenderData;

        public GLAvatar av;

        public GLMesh(string name)
            : base(name)
        {
        }

        public GLMesh(GLMesh source, GLAvatar av)
            : base(source.Name)
        {
            this.av = av;
            // Make a new GLMesh copy from the supplied source

            RenderData.Vertices = new float[source.RenderData.Vertices.Length];
            RenderData.Normals = new float[source.RenderData.Normals.Length];
            RenderData.TexCoords = new float[source.RenderData.TexCoords.Length];
            RenderData.Indices = new ushort[source.RenderData.Indices.Length];

            RenderData.weights = new float[source.RenderData.weights.Length];
            RenderData.skinJoints = new string[source.RenderData.skinJoints.Length];

            Array.Copy(source.RenderData.Vertices, RenderData.Vertices, source.RenderData.Vertices.Length);
            Array.Copy(source.RenderData.Normals, RenderData.Normals, source.RenderData.Normals.Length);

            Array.Copy(source.RenderData.TexCoords, RenderData.TexCoords, source.RenderData.TexCoords.Length);
            Array.Copy(source.RenderData.Indices, RenderData.Indices, source.RenderData.Indices.Length);
            Array.Copy(source.RenderData.weights, RenderData.weights, source.RenderData.weights.Length);
            Array.Copy(source.RenderData.skinJoints, RenderData.skinJoints, source.RenderData.skinJoints.Length);

            RenderData.Center = new Vector3(source.RenderData.Center);

            teFaceID = source.teFaceID;

            RotationAngles = new Vector3(source.RotationAngles);
            Scale = new Vector3(source.Scale);
            Position = new Vector3(source.Position);

            // We should not need to instance these the reference from the top should be constant
            _evp = source._evp;
            Morphs = source.Morphs;

            OrigRenderData.Indices = new ushort[source.RenderData.Indices.Length];
            OrigRenderData.TexCoords = new float[source.RenderData.TexCoords.Length];
            OrigRenderData.Vertices = new float[source.RenderData.Vertices.Length];
            OrigRenderData.Normals = new float[source.RenderData.Normals.Length];

            MorphRenderData.Vertices = new float[source.RenderData.Vertices.Length];
            MorphRenderData.Normals = new float[source.RenderData.Normals.Length];

            Array.Copy(source.RenderData.Vertices, OrigRenderData.Vertices, source.RenderData.Vertices.Length);
            Array.Copy(source.RenderData.Vertices, MorphRenderData.Vertices, source.RenderData.Vertices.Length);

            Array.Copy(source.RenderData.Normals, OrigRenderData.Normals, source.RenderData.Normals.Length);
            Array.Copy(source.RenderData.Normals, MorphRenderData.Normals, source.RenderData.Normals.Length);

            Array.Copy(source.RenderData.TexCoords, OrigRenderData.TexCoords, source.RenderData.TexCoords.Length);
            Array.Copy(source.RenderData.Indices, OrigRenderData.Indices, source.RenderData.Indices.Length);
        }

        public void setMeshPos(Vector3 pos)
        {
            Position = pos;

            // Force offset all vertices by this offset
            // this is required to force some meshes into the default T bind pose

            for (int vert = 0; vert < RenderData.Vertices.Length; vert = vert + 3)
            {
                RenderData.Vertices[vert] += pos.X;
                RenderData.Vertices[vert + 1] += pos.Y;
                RenderData.Vertices[vert + 2] += pos.Z;
            }


        }

        public void setMeshRot(Vector3 rot)
        {
            RotationAngles = rot;
        }

        public override void LoadMesh(string filename)
        {
            base.LoadMesh(filename);

            float minY, minZ;
            var minX = minY = minZ = float.MaxValue;
            float maxY, maxZ;
            var maxX = maxY = maxZ = float.MinValue;

            // Generate the vertex array
            RenderData.Vertices = new float[NumVertices * 3];
            RenderData.Normals = new float[NumVertices * 3];

            Quaternion quat = Quaternion.CreateFromEulers(0, 0, (float)(Math.PI / 4.0));

            int current = 0;
            for (int i = 0; i < NumVertices; i++)
            {

                RenderData.Normals[current] = Vertices[i].Normal.X;
                RenderData.Vertices[current++] = Vertices[i].Coord.X;
                RenderData.Normals[current] = Vertices[i].Normal.Y;
                RenderData.Vertices[current++] = Vertices[i].Coord.Y;
                RenderData.Normals[current] = Vertices[i].Normal.Z;
                RenderData.Vertices[current++] = Vertices[i].Coord.Z;

                if (Vertices[i].Coord.X < minX)
                    minX = Vertices[i].Coord.X;
                else if (Vertices[i].Coord.X > maxX)
                    maxX = Vertices[i].Coord.X;

                if (Vertices[i].Coord.Y < minY)
                    minY = Vertices[i].Coord.Y;
                else if (Vertices[i].Coord.Y > maxY)
                    maxY = Vertices[i].Coord.Y;

                if (Vertices[i].Coord.Z < minZ)
                    minZ = Vertices[i].Coord.Z;
                else if (Vertices[i].Coord.Z > maxZ)
                    maxZ = Vertices[i].Coord.Z;
            }

            // Calculate the center-point from the bounding box edges
            RenderData.Center = new Vector3((minX + maxX) / 2, (minY + maxY) / 2, (minZ + maxZ) / 2);

            // Generate the index array
            RenderData.Indices = new ushort[NumFaces * 3];
            current = 0;
            for (int i = 0; i < NumFaces; i++)
            {
                RenderData.Indices[current++] = (ushort)Faces[i].Indices[0];
                RenderData.Indices[current++] = (ushort)Faces[i].Indices[1];
                RenderData.Indices[current++] = (ushort)Faces[i].Indices[2];
            }

            // Generate the texcoord array
            RenderData.TexCoords = new float[NumVertices * 2];
            current = 0;
            for (int i = 0; i < NumVertices; i++)
            {
                RenderData.TexCoords[current++] = Vertices[i].TexCoord.X;
                RenderData.TexCoords[current++] = Vertices[i].TexCoord.Y;
            }

            RenderData.weights = new float[NumVertices];
            for (int i = 0; i < NumVertices; i++)
            {
                RenderData.weights[i] = Vertices[i].Weight;
            }

            RenderData.skinJoints = new string[SkinJoints.Length + 3];
            for (int i = 1; i < SkinJoints.Length; i++)
            {
                RenderData.skinJoints[i] = SkinJoints[i];
            }


        }

        public new void LoadLODMesh(int level, string filename)
        {
            LODMesh lod = new LODMesh();
            lod.LoadMesh(filename);
            LodMeshes[level] = lod;
        }

        public void applyjointweights()
        {

            /*Each weight actually contains two pieces of information. 
             * The number to the left of the decimal point is the index of the joint and also 
             * implicitly indexes to the following joint. The actual weight is to the right of 
             * the decimal point and interpolates between these two joints. The index is into an 
             * "expanded" list of joints, not just a linear array of the joints as defined in the 
             * skeleton file. In particular, any joint that has more than one child will be repeated 
             * in the list for each of its children.
             */

            float weight = -9999;
            int jointindex = 0;
            float factor;

            Bone ba = null;
            Bone bb = null;

            for (int v = 0, x = 0; v < RenderData.Vertices.Length; v = v + 3, x++)
            {
                if (weight != RenderData.weights[x])
                {

                    jointindex = (int)Math.Floor(weight = RenderData.weights[x]);
                    factor = RenderData.weights[x] - jointindex;
                    weight = weight - jointindex;

                    string jointname = "", jointname2 = "";

                    switch (Name)
                    {
                        case "upperBodyMesh":
                            jointname = skeleton.mUpperMeshMapping[jointindex];
                            jointindex++;
                            jointname2 = skeleton.mUpperMeshMapping[jointindex];
                            break;

                        case "lowerBodyMesh":
                            jointname = skeleton.mLowerMeshMapping[jointindex];
                            jointindex++;
                            jointname2 = skeleton.mLowerMeshMapping[jointindex];
                            break;

                        case "headMesh":
                            jointname = skeleton.mHeadMeshMapping[jointindex];
                            jointindex++;
                            jointname2 = skeleton.mHeadMeshMapping[jointindex];
                            break;

                        case "eyeBallRightMesh":
                            jointname = "mHead";
                            jointname2 = "mEyeRight";
                            break;

                        case "eyeBallLeftMesh":
                            jointname = "mHead";
                            jointname2 = "mEyeLeft";
                            break;

                        case "eyelashMesh":
                        case "hairMesh":
                            jointname = "mHead";
                            jointname2 = "mSkull";
                            break;

                        default:
                            return;

                    }

                    ba = av.skel.mBones[jointname];
                    
                    bb = jointname2 == "" ? null : av.skel.mBones[jointname2];
                }

                //Special cases 0 is not used
                // ON upper torso 5 and 10 are not used
                // 4 is neck and 6 and 11 are the left and right collar bones


                //TODO use the SRT matrix to do this!

                Vector3 lerpa;
                Vector3 offseta;
                Quaternion rota;

                Vector3 lerpb;
                Vector3 offsetb;
                Quaternion rotb;

                Vector3 pos;

                Vector3 posa = new Vector3(MorphRenderData.Vertices[v], MorphRenderData.Vertices[v + 1], MorphRenderData.Vertices[v + 2]);


                if (bb != null)
                {
                    lerpa = ba.getDeltaOffset();
                    offseta = ba.getTotalOffset();
                    rota = ba.getTotalRotation();

                    lerpb = bb.getDeltaOffset();
                    offsetb = bb.getTotalOffset();
                    rotb = bb.getTotalRotation();

                    Vector3 posb = new Vector3(MorphRenderData.Vertices[v], MorphRenderData.Vertices[v + 1], MorphRenderData.Vertices[v + 2]);

                    //move back to mesh local coords
                    posa = posa - offseta;

                    // apply rotated offset
                    posa = ((posa + lerpa) * ba.scale) * rota;
                    //move back to avatar local coords
                    posa = posa + offseta;

                    //move back to mesh local coords
                    posb = posb - offsetb;

                    // apply rotated offset
                    posb = ((posb + lerpb) * bb.scale) * rotb;
                    //move back to avatar local coords
                    posb = posb + offsetb;

                    //LERP the two points to produce smooth skinning ;-)
                    pos = Vector3.Lerp(posa, posb, weight);

                }
                else
                {
                    lerpa = ba.getDeltaOffset();
                    offseta = ba.getTotalOffset();
                    rota = ba.getTotalRotation();

                    //move back to mesh local coords
                    posa = posa - offseta;

                    // apply rotated offset
                    posa = ((posa + lerpa) * ba.scale) * rota;
                    //move back to avatar local coords
                    posa = posa + offseta;

                    //Only one bone contributing so its 100% that one.
                    pos = posa;

                }

                RenderData.Vertices[v] = pos.X;
                RenderData.Vertices[v + 1] = pos.Y;
                RenderData.Vertices[v + 2] = pos.Z;
            }
        }

        public void resetallmorphs()
        {
            for (int i = 0; i < OrigRenderData.Vertices.Length / 3; i++)
            {

                MorphRenderData.Vertices[i * 3] = OrigRenderData.Vertices[i * 3];
                MorphRenderData.Vertices[(i * 3) + 1] = OrigRenderData.Vertices[i * 3 + 1];
                MorphRenderData.Vertices[(i * 3) + 2] = OrigRenderData.Vertices[i * 3 + 2];

                MorphRenderData.Normals[i * 3] = OrigRenderData.Normals[i * 3];
                MorphRenderData.Normals[(i * 3) + 1] = OrigRenderData.Normals[i * 3 + 1];
                MorphRenderData.Normals[(i * 3) + 2] = OrigRenderData.Normals[i * 3 + 2];

                RenderData.TexCoords[i * 2] = OrigRenderData.TexCoords[i * 2];
                RenderData.TexCoords[(i * 2) + 1] = OrigRenderData.TexCoords[i * 2 + 1];

            }

        }

        public void morphmesh(Morph morph, float weight)
        {
            // Logger.Log(String.Format("Applying morph {0} weight {1}",morph.Name,weight),Helpers.LogLevel.Debug);

            for (int v = 0; v < morph.NumVertices; v++)
            {
                MorphVertex mvx = morph.Vertices[v];

                uint i = mvx.VertexIndex;

                MorphRenderData.Vertices[i * 3] = MorphRenderData.Vertices[i * 3] + mvx.Coord.X * weight;
                MorphRenderData.Vertices[(i * 3) + 1] = MorphRenderData.Vertices[i * 3 + 1] + mvx.Coord.Y * weight;
                MorphRenderData.Vertices[(i * 3) + 2] = MorphRenderData.Vertices[i * 3 + 2] + mvx.Coord.Z * weight;

                MorphRenderData.Normals[i * 3] = MorphRenderData.Normals[i * 3] + mvx.Normal.X * weight;
                MorphRenderData.Normals[(i * 3) + 1] = MorphRenderData.Normals[(i * 3) + 1] + mvx.Normal.Y * weight;
                MorphRenderData.Normals[(i * 3) + 2] = MorphRenderData.Normals[(i * 3) + 2] + mvx.Normal.Z * weight;

                RenderData.TexCoords[i * 2] = OrigRenderData.TexCoords[i * 2] + mvx.TexCoord.X * weight;
                RenderData.TexCoords[(i * 2) + 1] = OrigRenderData.TexCoords[i * 2 + 1] + mvx.TexCoord.Y * weight;

            }
        }
    }

    public class GLAvatar
    {
        private static Dictionary<string, GLMesh> _defaultmeshes = new Dictionary<string, GLMesh>();
        public Dictionary<string, GLMesh> _meshes = new Dictionary<string, GLMesh>();

        public skeleton skel = new skeleton();
        public static Dictionary<int, attachment_point> attachment_points = new Dictionary<int, attachment_point>();

        public bool _wireframe = true;
        public bool _showSkirt = false;

        public VisualParamEx.EparamSex msex;

        public byte[] VisualAppearanceParameters = new byte[1024];
        static bool lindenMeshesLoaded = false;

        public GLAvatar()
        {
            lock (_defaultmeshes) foreach (var kvp in _defaultmeshes)
            {
                GLMesh mesh = new GLMesh(kvp.Value, this); // Instance our meshes
                _meshes.Add(kvp.Key, mesh);

            }
        }

        public static void dumptweaks()
        {

            for (int x = 0; x < VisualParamEx.TweakableParams.Count; x++)
            {
                VisualParamEx vpe = (VisualParamEx)VisualParamEx.TweakableParams.GetByIndex(x);
                Console.WriteLine($"{x} is {vpe.Name}");
            }

        }

        public static void loadlindenmeshes2(string LODfilename)
        {
            // Already have mashes loaded?
            if (lindenMeshesLoaded) return;

            attachment_points.Clear();


            string basedir = Path.Combine(OpenMetaverse.Settings.RESOURCE_DIR, "character");

            XmlDocument lad = new XmlDocument();
            lad.Load(Path.Combine(basedir, LODfilename));

            // First, read the skeleton section this contains attachment point info and the bone deform info for visual params
            // And load the skeleton file in to the bones class

            XmlNodeList skeleton = lad.GetElementsByTagName("skeleton");
            string skeletonfilename = skeleton[0].Attributes?.GetNamedItem("file_name").Value;
            Bone.loadBones(skeletonfilename);

            // Next read all the skeleton child nodes, we have attachment points and bone deform params
            // attachment points are an offset and rotation from a bone location
            // the name of the bone they reference is the joint paramater
            // params in the skeleton nodes are bone deforms, eg leg length changes the scale of the leg bones

            foreach (XmlNode skeletonnode in skeleton[0].ChildNodes)
            {
                if (skeletonnode.Name == "attachment_point")
                {
                    attachment_point point = new attachment_point(skeletonnode);
                    attachment_points.Add(point.id, point);
                }
            }

            // Parse all visual paramaters in one go
            // we can deduce type on the fly
            XmlNodeList paramss = lad.GetElementsByTagName("param");
            foreach (XmlNode paramNode in paramss)
            {
                VisualParamEx vp = new VisualParamEx(paramNode);
            }

            //Now we parse the mesh nodes, mesh nodes reference a particular LLM file with a LOD

            XmlNodeList meshes = lad.GetElementsByTagName("mesh");
            foreach (XmlNode meshNode in meshes)
            {
                string type = meshNode.Attributes?.GetNamedItem("type").Value;
                int lod = Int32.Parse(meshNode.Attributes?.GetNamedItem("lod").Value);
                string fileName = meshNode.Attributes?.GetNamedItem("file_name").Value;

                GLMesh mesh = null;
                lock (_defaultmeshes)
                    mesh = (_defaultmeshes.ContainsKey(type) ? _defaultmeshes[type] : new GLMesh(type));

                // Set up the texture elements for each mesh
                // And hack the eyeball position
                switch (mesh.Name)
                {
                    case "lowerBodyMesh":
                        mesh.teFaceID = (int)AvatarTextureIndex.LowerBaked;
                        break;

                    case "upperBodyMesh":
                        mesh.teFaceID = (int)AvatarTextureIndex.UpperBaked;
                        break;

                    case "headMesh":
                        mesh.teFaceID = (int)AvatarTextureIndex.HeadBaked;
                        break;

                    case "hairMesh":
                        mesh.teFaceID = (int)AvatarTextureIndex.HairBaked;
                        break;

                    case "eyelashMesh":
                        mesh.teFaceID = (int)AvatarTextureIndex.HeadBaked;
                        break;

                    case "eyeBallRightMesh":
                        mesh.teFaceID = (int)AvatarTextureIndex.EyesBaked;
                        break;

                    case "eyeBallLeftMesh":
                        mesh.teFaceID = (int)AvatarTextureIndex.EyesBaked;
                        break;

                    case "skirtMesh":
                        mesh.teFaceID = (int)AvatarTextureIndex.SkirtBaked;
                        break;

                    default:
                        mesh.teFaceID = 0;
                        break;
                }

                if (lod == 0)
                    mesh.LoadMesh(Path.Combine(basedir, fileName));
                else
                    mesh.LoadLODMesh(lod, Path.Combine(basedir, fileName));

                if (lod == 0)
                {
                    switch (mesh.Name)
                    {
                        case "eyeBallLeftMesh":
                            lock (Bone.mBones) mesh.setMeshPos(Bone.mBones["mEyeLeft"].getTotalOffset());
                            break;

                        case "eyeBallRightMesh":
                            lock (Bone.mBones) mesh.setMeshPos(Bone.mBones["mEyeRight"].getTotalOffset());
                            break;

                        case "eyelashMesh":
                            lock (Bone.mBones) mesh.setMeshPos(Bone.mBones["mHead"].getTotalOffset());
                            break;

                        case "hairMesh":
                            //mesh.setMeshPos(Bone.mBones["mHead"].getTotalOffset());
                            break;
                    }
                }

                lock (_defaultmeshes) _defaultmeshes[type] = mesh;

            }

            lindenMeshesLoaded = true;
        }

        public void applyMorph(Avatar av, int param, float weight)
        {
            VisualParamEx vpx;

            if (VisualParamEx.AllParams.TryGetValue(param, out vpx))
            {
                applyMorph(vpx, av, weight);

                // a morph ID may apply to more than one mesh (duplicate VP IDs)
                // in this case also apply to all other identical IDs
                foreach (VisualParamEx cvpx in vpx.IdenticalIds)
                {
                    applyMorph(cvpx, av, weight);
                }
            }
        }

        public void applyMorph(VisualParamEx vpx, Avatar av, float weight)
        {

            weight = Utils.Clamp(weight, 0.0f, 1.0f);
            float value = Utils.Lerp(vpx.MinValue, vpx.MaxValue, weight);

            // don't do anything for less than 1% change
            if (value > -0.001 && value < 0.001)
                return;

            // Morphs are mesh deforms
            if (vpx.pType == VisualParamEx.ParamType.TYPE_MORPH)
            {
                // Its a morph
                GLMesh mesh;
                if (_meshes.TryGetValue(vpx.MorphMesh, out mesh))
                {
                    foreach (LindenMesh.Morph morph in mesh.Morphs) //optimise me to a dictionary
                    {
                        if (morph.Name == vpx.Name)
                        {
                            if (mesh.Name == "skirtMesh" && _showSkirt == false)
                                return;

                            // Logger.Log(String.Format("Applying morph {0} ID {2} weight {1} mesh {3}",morph.Name,weight,vpx.ParamID,mesh.Name),Helpers.LogLevel.Debug);

                            mesh.morphmesh(morph, value);

                        }
                    }
                }
            }

            // Driver type
            // A driver drives multiple slave visual paramaters
            if (vpx.pType == VisualParamEx.ParamType.TYPE_DRIVER)
            {
                foreach (VisualParamEx.driven child in vpx.ChildParams)
                {

                    /***** BEGIN UNGRACEFULL CODE STEALING ******/

                    //	driven    ________
                    //	^        /|       |\       ^
                    //	|       / |       | \      |
                    //	|      /  |       |  \     |
                    //	|     /   |       |   \    |
                    //	|    /    |       |    \   |
                    //-------|----|-------|----|-------> driver
                    //  | min1   max1    max2  min2


                    if (child.hasMinMax == false)
                    {
                        applyMorph(av, child.id, weight);
                        continue;
                    }

                    float driven_weight = vpx.DefaultValue;
                    float driven_max = VisualParamEx.AllParams[child.id].MaxValue;
                    float driven_min = VisualParamEx.AllParams[child.id].MinValue;
                    float input_weight = weight;

                    float min_weight = vpx.MinValue;
                    float max_weight = vpx.MaxValue;

                    if (input_weight <= child.min1)
                    {
                        if (child.min1 == child.max1 &&
                            child.min1 <= min_weight)
                        {
                            driven_weight = driven_max;
                        }
                        else
                        {
                            driven_weight = driven_min;
                        }
                    }
                    else
                    {
                        if (input_weight <= child.max1)
                        {
                            float t = (input_weight - child.min1)/(child.max1 - child.min1);
                            driven_weight = driven_min + t*(driven_max - driven_min);
                        }
                        else if (input_weight <= child.max2)
                        {
                            driven_weight = driven_max;
                        }
                        else if (input_weight <= child.min2)
                        {
                            float t = (input_weight - child.max2)/(child.min2 - child.max2);
                            driven_weight = driven_max + t*(driven_min - driven_max);
                        }
                        else
                        {
                            driven_weight = child.max2 >= max_weight ? driven_max : driven_min;
                        }
                    }


                    /***** END UNGRACEFULL CODE STEALING ******/

                    applyMorph(av, child.id, driven_weight);

                }

                return;
            }

            //Is this a bone deform?
            if (vpx.pType == VisualParamEx.ParamType.TYPE_BONEDEFORM)
            {
                //  scale="0 0 .3" />
                //   value_min="-1"
                // value_max="1"

                foreach (var kvp in vpx.BoneDeforms)
                {
                    skel.scalebone(kvp.Key, Vector3.One + (kvp.Value.scale * value));
                    skel.offsetbone(kvp.Key, kvp.Value.offset * value);
                }
            }
        }

        public void morph(Avatar av)
        {

            if (av.VisualParameters == null)
                return;

            ThreadPool.QueueUserWorkItem(sync =>
            {
                int x = 0;

                // We need a lock here as we may get multiple packets thrown at us and we should at least
                // process them in turn not 1/2 process one then start to process the next. 
                // That said av might not be the best lock object, but it will do for the moment
                lock (av)
                {
                    if (av.VisualParameters.Length > 123)
                    {
                        msex = av.VisualParameters[31] > 127 ? VisualParamEx.EparamSex.SEX_MALE : VisualParamEx.EparamSex.SEX_FEMALE;
                    }

                    foreach (GLMesh mesh in _meshes.Values)
                    {
                        mesh.resetallmorphs();
                    }

                    skel.resetbonescales();

                    foreach (byte vpvalue in av.VisualParameters)
                    {
                        if (x >= VisualParamEx.TweakableParams.Count)
                        {
                            //Logger.Log("Two many visual paramaters in Avatar appearance", Helpers.LogLevel.Warning);
                            break;
                        }

                        VisualParamEx vpe = (VisualParamEx)VisualParamEx.TweakableParams.GetByIndex(x);

                        if (vpe.sex != VisualParamEx.EparamSex.SEX_BOTH && vpe.sex != msex)
                        {
                            x++;
                            continue;
                        }

                        VisualAppearanceParameters[x] = vpvalue;

                        float value = (vpvalue / 255.0f);
                        applyMorph(av, vpe.ParamID, value);

                        x++;
                    }

                    skel.mNeedsMeshRebuild = true;
                    // Don't update actual meshes here anymore, we do it every frame because of animation anyway

                }
            });
        }
    }

    public class joint
    {
        public Vector3 offset;
        public Quaternion rotation;
    }

    public class animationwrapper
    {
        public BinBVHAnimationReader anim;
        public float mRunTime;
        public UUID mAnimation;
        public bool mPotentialyDead = false;

        public enum animstate
        {
            STATE_WAITINGASSET,
            STATE_EASEIN,
            STATE_EASEOUT,
            STATE_PLAY,
            STATE_STOP
        }

        public animstate playstate;

        public animationwrapper(BinBVHAnimationReader anim)
        {
            this.anim = anim;
            playstate = animstate.STATE_EASEIN;
            mRunTime = 0;
        }

        public animationwrapper(UUID key)
        {
            mAnimation = key;
            playstate = animstate.STATE_WAITINGASSET;
            mRunTime = 0;
        }

        public void stopanim()
        {
            //Logger.Log(string.Format("Animation {0} marked as stopped",mAnimation),Helpers.LogLevel.Info);
            playstate = animstate.STATE_STOP;
        }
    }

    public class skeleton
    {
        public Dictionary<string, Bone> mBones;
        private Dictionary<string, int> mPriority = new Dictionary<string, int>();
        private Dictionary<string, joint> jointdeforms = new Dictionary<string, joint>();

        public static Dictionary<int, string> mUpperMeshMapping = new Dictionary<int, string>();
        public static Dictionary<int, string> mLowerMeshMapping = new Dictionary<int, string>();
        public static Dictionary<int, string> mHeadMeshMapping = new Dictionary<int, string>();

        // public List<BinBVHAnimationReader> mAnimations = new List<BinBVHAnimationReader>();
        public Dictionary<UUID, animationwrapper> mAnimationsWrapper = new Dictionary<UUID, animationwrapper>();

        public static Dictionary<UUID, RenderAvatar> mAnimationTransactions = new Dictionary<UUID, RenderAvatar>();

        public static Dictionary<UUID, BinBVHAnimationReader> mAnimationCache = new Dictionary<UUID, BinBVHAnimationReader>();

        public bool mNeedsUpdate = false;
        public bool mNeedsMeshRebuild = true;


        public struct binBVHJointState
        {
            public float currenttime_rot;
            public int lastkeyframe_rot;
            public int nextkeyframe_rot;

            public float currenttime_pos;
            public int lastkeyframe_pos;
            public int nextkeyframe_pos;

            public int pos_loopinframe;
            public int pos_loopoutframe;

            public int rot_loopinframe;
            public int rot_loopoutframe;

            public Quaternion easeoutrot;
            public float easeoutfactor;

        }


        public skeleton()
        {

            mBones = new Dictionary<string, Bone>();

            lock (Bone.mBones) foreach (Bone src in Bone.mBones.Values)
            {
                Bone newbone = new Bone(src);
                mBones.Add(newbone.name, newbone);
            }

            //rebuild the skeleton structure on the new copy
            foreach (Bone src in mBones.Values)
            {
                if (src.mParentBone != null)
                {
                    src.parent = mBones[src.mParentBone];
                    src.parent.children.Add(src);
                }
            }

            //FUDGE
            if (mUpperMeshMapping.Count == 0)
            {
                mUpperMeshMapping.Add(1, "mPelvis");
                mUpperMeshMapping.Add(2, "mTorso");
                mUpperMeshMapping.Add(3, "mChest");
                mUpperMeshMapping.Add(4, "mNeck");
                mUpperMeshMapping.Add(5, "mNeck");
                mUpperMeshMapping.Add(6, "mCollarLeft");
                mUpperMeshMapping.Add(7, "mShoulderLeft");
                mUpperMeshMapping.Add(8, "mElbowLeft");
                mUpperMeshMapping.Add(9, "mWristLeft");
                mUpperMeshMapping.Add(10, "mNeck");  // this case might fail for mWriteLeft and mNeck acting together?
                mUpperMeshMapping.Add(11, "mCollarRight");
                mUpperMeshMapping.Add(12, "mShoulderRight");
                mUpperMeshMapping.Add(13, "mElbowRight");
                mUpperMeshMapping.Add(14, "mWristRight");
                mUpperMeshMapping.Add(15, "");

                mLowerMeshMapping.Add(1, "mPelvis");
                mLowerMeshMapping.Add(2, "mHipRight");
                mLowerMeshMapping.Add(3, "mKneeRight");
                mLowerMeshMapping.Add(4, "mAnkleRight");
                mLowerMeshMapping.Add(5, "mPelvis");
                mLowerMeshMapping.Add(6, "mHipLeft");
                mLowerMeshMapping.Add(7, "mKneeLeft");
                mLowerMeshMapping.Add(8, "mAnkleLeft");
                mLowerMeshMapping.Add(9, "");

                mHeadMeshMapping.Add(1, "mNeck");
                mHeadMeshMapping.Add(2, "mHead");
                mHeadMeshMapping.Add(3, "");

            }

        }

        public void processAnimation(UUID mAnimID)
        {
            if (mAnimationsWrapper.ContainsKey(mAnimID))
            {
                // Its an existing animation, we may need to do a seq update but we don't yet
                // support that
                mAnimationsWrapper[mAnimID].playstate = animationwrapper.animstate.STATE_WAITINGASSET;
                mAnimationsWrapper[mAnimID].mPotentialyDead = false;
            }
            else
            {
                // Its a new animation do the decode dance
                animationwrapper aw = new animationwrapper(mAnimID);
                mAnimationsWrapper.Add(mAnimID, aw);

            }
        }

        public void resetbonescales()
        {
            foreach (var src in mBones)
            {
                src.Value.scale = Vector3.One;
                src.Value.offset_pos = Vector3.Zero;
            }
        }

        public void deformbone(string name, Vector3 pos, Quaternion rotation)
        {
            Bone bone;
            if (mBones.TryGetValue(name, out bone))
            {
                bone.deformbone(pos, rotation);
            }
        }

        public void scalebone(string name, Vector3 scale)
        {

            Bone bone;
            if (mBones.TryGetValue(name, out bone))
            {
                // Logger.Log(String.Format("scalebone() {0} {1}", name, scale.ToString()),Helpers.LogLevel.Info);
                bone.scalebone(scale);
            }
        }

        public void offsetbone(string name, Vector3 offset)
        {
            Bone bone;
            if (mBones.TryGetValue(name, out bone))
            {
                bone.offsetbone(offset);
            }
        }

        //TODO check offset and rot calcuations should each offset be multiplied by its parent rotation in
        // a standard child/parent rot/offset way?
        public Vector3 getOffset(string bonename)
        {
            Bone b;
            return mBones.TryGetValue(bonename, out b) ? (b.getTotalOffset()) : Vector3.Zero;
        }

        public Quaternion getRotation(string bonename)
        {
            Bone b;
            return mBones.TryGetValue(bonename, out b) ? (b.getTotalRotation()) : Quaternion.Identity;
        }


        public void flushanimations()
        {
            lock (mAnimationsWrapper)
            {
                List<UUID> kills = new List<UUID>();
                foreach (animationwrapper ar in mAnimationsWrapper.Values)
                {
                    if (ar.playstate == animationwrapper.animstate.STATE_STOP)
                    {
                        kills.Add(ar.mAnimation);
                    }

                    ar.mPotentialyDead = true;
                }

                foreach (UUID key in kills)
                {
                    mAnimationsWrapper.Remove(key);
                    //Logger.Log(string.Format("Removing dead animation {0} from av", key), Helpers.LogLevel.Info);
                }
            }
        }

        public void flushanimationsfinal()
        {
            lock (mAnimationsWrapper)
            {
                foreach (animationwrapper ar in mAnimationsWrapper.Values)
                {
                    if (ar.mPotentialyDead)
                    {
                        // Logger.Log(string.Format("Animation {0} is being marked for easeout (dead)",ar.mAnimation.ToString()),Helpers.LogLevel.Info);
                        // Should we just stop dead? i think not it may get jerky
                        ar.playstate = animationwrapper.animstate.STATE_EASEOUT;
                        ar.mRunTime = 0; //fix me nasty hack
                    }
                }
            }

        }

        // Add animations to the global decoded list
        // TODO garbage collect unused animations somehow
        public static void addanimation(OpenMetaverse.Assets.Asset asset, UUID tid, BinBVHAnimationReader b, UUID animKey)
        {
            RenderAvatar av;
            mAnimationTransactions.TryGetValue(tid, out av);
            if (av == null)
                return;

            mAnimationTransactions.Remove(tid);

            if (asset != null)
            {
                b = new BinBVHAnimationReader(asset.AssetData);
                mAnimationCache[asset.AssetID] = b;
                Logger.Log($"Adding new decoded animaton known animations {asset.AssetID}", Helpers.LogLevel.Debug);
            }

            if (!av.glavatar.skel.mAnimationsWrapper.ContainsKey(animKey))
            {
                Logger.Log($"Animation {animKey} is not in mAnimationsWrapper! ", Helpers.LogLevel.Warning);
                return;
            }

            // This sets the anim in the wrapper class;
            av.glavatar.skel.mAnimationsWrapper[animKey].anim = b;

            int pos = 0;
            foreach (binBVHJoint joint in b.joints)
            {
                binBVHJointState state;

                state.lastkeyframe_rot = 0;
                state.nextkeyframe_rot = 1;

                state.lastkeyframe_pos = 0;
                state.nextkeyframe_pos = 1;

                state.currenttime_rot = 0;
                state.currenttime_pos = 0;

                state.pos_loopinframe = 0;
                state.pos_loopoutframe = joint.positionkeys.Length - 1;

                state.rot_loopinframe = 0;
                state.rot_loopoutframe = joint.rotationkeys.Length - 1;

                state.easeoutfactor = 1.0f;
                state.easeoutrot = Quaternion.Identity;

                if (b.Loop)
                {
                    int frame = 0;
                    foreach (binBVHJointKey key in joint.rotationkeys)
                    {
                        if (key.time == b.InPoint)
                        {
                            state.rot_loopinframe = frame;
                        }

                        if (key.time == b.OutPoint)
                        {
                            state.rot_loopoutframe = frame;
                        }

                        frame++;
                    }

                    frame = 0;
                    foreach (binBVHJointKey key in joint.positionkeys)
                    {
                        if (key.time == b.InPoint)
                        {
                            state.pos_loopinframe = frame;
                        }

                        if (key.time == b.OutPoint)
                        {
                            state.pos_loopoutframe = frame;
                        }

                        frame++;
                    }

                }

                b.joints[pos].Tag = state;
                pos++;
            }

            av.glavatar.skel.mAnimationsWrapper[animKey].playstate = animationwrapper.animstate.STATE_EASEIN;
            recalcpriorities(av);

        }

        public static void recalcpriorities(RenderAvatar av)
        {

            lock (av.glavatar.skel.mAnimationsWrapper)
            {
                //av.glavatar.skel.mAnimationsWrapper.Add(new animationwrapper(b));

                //pre calculate all joint priorities here
                av.glavatar.skel.mPriority.Clear();

                foreach (var kvp in av.glavatar.skel.mAnimationsWrapper)
                {
                    int jpos = 0;
                    animationwrapper ar = kvp.Value;
                    if (ar.anim == null)
                        continue;

                    foreach (binBVHJoint joint in ar.anim.joints)
                    {
                        if (ar.anim == null)
                            continue;

                        //warning struct copy non reference
                        binBVHJointState state = (binBVHJointState)ar.anim.joints[jpos].Tag;

                        if (ar.playstate == animationwrapper.animstate.STATE_STOP || ar.playstate == animationwrapper.animstate.STATE_EASEOUT)
                            continue;

                        //FIX ME need to consider ease out here on priorities somehow


                        int prio = 0;
                        //Quick hack to stack animations in the correct order
                        //TODO we need to do this per joint as they all have their own priorities as well ;-(
                        if (av.glavatar.skel.mPriority.TryGetValue(joint.Name, out prio))
                        {
                            if (prio > (ar.anim.Priority))
                                continue;
                        }

                        av.glavatar.skel.mPriority[joint.Name] = ar.anim.Priority;

                        jpos++;

                        //av.glavatar.skel.mAnimationsWrapper[kvp.Key].playstate = animationwrapper.animstate.STATE_EASEIN;

                    }
                }
            }

        }

        public void animate(float lastframetime)
        {

            lock (mAnimationsWrapper)
            {

                jointdeforms.Clear();

                foreach (animationwrapper ar in mAnimationsWrapper.Values)
                {

                    if (ar.playstate == animationwrapper.animstate.STATE_WAITINGASSET)
                        continue;

                    if (ar.anim == null)
                        continue;

                    ar.mRunTime += lastframetime;

                    // EASE FACTORS
                    // Caclulate ease factors now they are common to all joints in a given animation
                    float factor = 1.0f;

                    if (ar.playstate == animationwrapper.animstate.STATE_EASEIN)
                    {
                        if (ar.mRunTime >= ar.anim.EaseInTime)
                        {
                            ar.playstate = animationwrapper.animstate.STATE_PLAY;
                            //Console.WriteLine(String.Format("{0} Now in STATE_PLAY", ar.mAnimation));
                        }
                        else
                        {
                            factor = 1.0f - ((ar.anim.EaseInTime - ar.mRunTime) / ar.anim.EaseInTime);
                        }

                        //Console.WriteLine(String.Format("EASE IN {0} {1}",factor.ToString(),ar.mAnimation));
                    }

                    if (ar.playstate == animationwrapper.animstate.STATE_EASEOUT)
                    {

                        if (ar.mRunTime >= ar.anim.EaseOutTime)
                        {
                            ar.stopanim();
                            factor = 0;
                            //Console.WriteLine(String.Format("{0} Now in STATE_STOP", ar.mAnimation));

                        }
                        else
                        {
                            factor = 1.0f - (ar.mRunTime / ar.anim.EaseOutTime);

                        }

                        //Console.WriteLine(String.Format("EASE OUT {0} {1}", factor.ToString(), ar.mAnimation));
                    }

                    // we should not need this, this implies bad math above


                    factor = Utils.Clamp(factor, 0.0f, 1.0f);
                    //factor = 1.0f;

                    //END EASE FACTORS


                    int jpos = 0;
                    foreach (binBVHJoint joint in ar.anim.joints)
                    {
                        bool easeoutset = false;
                        //warning struct copy non reference
                        binBVHJointState state = (binBVHJointState)ar.anim.joints[jpos].Tag;

                        if (ar.playstate == animationwrapper.animstate.STATE_STOP)
                            continue;

                        int prio = 0;
                        //Quick hack to stack animations in the correct order
                        //TODO we need to do this per joint as they all have their own priorities as well ;-(
                        if (mPriority.TryGetValue(joint.Name, out prio))
                        {
                            //if (prio > (ar.anim.Priority))
                            //continue;
                        }

                        Vector3 poslerp = Vector3.Zero;
                        Quaternion rotlerp = Quaternion.Identity;

                        // Position
                        if (ar.anim.joints[jpos].positionkeys.Length >= 2 && joint.Name == "mPelvis")
                        {

                            //Console.WriteLine("Animate time " + state.currenttime_pos.ToString());

                            state.currenttime_pos += lastframetime;

                            float currentime = state.currenttime_pos;
                            bool overrun = false;

                            if (state.currenttime_pos > ar.anim.OutPoint)
                            {
                                //overrun state
                                int itterations = (int)(state.currenttime_pos / ar.anim.OutPoint) + 1;
                                state.currenttime_pos = currentime = ar.anim.InPoint + ((ar.anim.OutPoint - ar.anim.InPoint) - (((ar.anim.OutPoint - ar.anim.InPoint) * itterations) - state.currenttime_pos));
                                overrun = true;
                            }

                            binBVHJointKey pos_next = ar.anim.joints[jpos].positionkeys[state.nextkeyframe_pos];
                            binBVHJointKey pos_last = ar.anim.joints[jpos].positionkeys[state.lastkeyframe_pos];

                            // if the current time > than next key frame time we move keyframes
                            if (currentime >= pos_next.time || overrun)
                            {

                                //Console.WriteLine("bump");
                                state.lastkeyframe_pos++;
                                state.nextkeyframe_pos++;

                                if (ar.anim.Loop)
                                {
                                    if (state.nextkeyframe_pos > state.pos_loopoutframe)
                                        state.nextkeyframe_pos = state.pos_loopinframe;

                                    if (state.lastkeyframe_pos > state.pos_loopoutframe)
                                        state.lastkeyframe_pos = state.pos_loopinframe;


                                    if (state.nextkeyframe_pos >= ar.anim.joints[jpos].positionkeys.Length)
                                        state.nextkeyframe_pos = state.pos_loopinframe;

                                    if (state.lastkeyframe_pos >= ar.anim.joints[jpos].positionkeys.Length)
                                        state.lastkeyframe_pos = state.pos_loopinframe;

                                }
                                else
                                {
                                    if (state.nextkeyframe_pos >= ar.anim.joints[jpos].positionkeys.Length)
                                        state.nextkeyframe_pos = ar.anim.joints[jpos].positionkeys.Length - 1;

                                    if (state.lastkeyframe_pos >= ar.anim.joints[jpos].positionkeys.Length)
                                    {
                                        state.lastkeyframe_pos = ar.anim.joints[jpos].positionkeys.Length - 1;

                                        ar.playstate = animationwrapper.animstate.STATE_EASEOUT;
                                        //animation over
                                    }
                                }
                            }

                            //if (pos_next.time == pos_last.time)
                            //{
                            //    ar.playstate = animationwrapper.animstate.STATE_EASEOUT;
                            //}

                            // update the pointers incase they have been moved
                            pos_next = ar.anim.joints[jpos].positionkeys[state.nextkeyframe_pos];
                            pos_last = ar.anim.joints[jpos].positionkeys[state.lastkeyframe_pos];

                            // TODO the lerp/delta is faulty
                            // it is not going to handle loop points when we wrap around as last will be > next
                            // it also fails when currenttime < last_time which occurs as keyframe[0] is not exactly at
                            // t(0).
                            float delta = (state.currenttime_pos - pos_last.time) / (pos_next.time - pos_last.time);

                            delta = Utils.Clamp(delta, 0f, 1f);
                            poslerp = Vector3.Lerp(pos_last.key_element, pos_next.key_element, delta) * factor;

                            //Console.WriteLine(string.Format("Time {0} {1} {2} {3} {4}", state.currenttime_pos, delta, poslerp.ToString(), state.lastkeyframe_pos, state.nextkeyframe_pos));

                        }

                        // end of position

                        //rotation

                        if (ar.anim.joints[jpos].rotationkeys.Length >= 2)
                        {

                            state.currenttime_rot += lastframetime;

                            float currentime = state.currenttime_rot;
                            bool overrun = false;

                            if (state.currenttime_rot > ar.anim.OutPoint)
                            {
                                //overrun state
                                int itterations = (int)(state.currenttime_rot / ar.anim.OutPoint) + 1;
                                state.currenttime_rot = currentime = ar.anim.InPoint + ((ar.anim.OutPoint - ar.anim.InPoint) - (((ar.anim.OutPoint - ar.anim.InPoint) * itterations) - state.currenttime_rot));
                                overrun = true;
                            }

                            binBVHJointKey rot_next = ar.anim.joints[jpos].rotationkeys[state.nextkeyframe_rot];
                            binBVHJointKey rot_last = ar.anim.joints[jpos].rotationkeys[state.lastkeyframe_rot];

                            // if the current time > than next key frame time we move keyframes
                            if (currentime >= rot_next.time || overrun)
                            {

                                state.lastkeyframe_rot++;
                                state.nextkeyframe_rot++;

                                if (ar.anim.Loop)
                                {
                                    if (state.nextkeyframe_rot > state.rot_loopoutframe)
                                        state.nextkeyframe_rot = state.rot_loopinframe;

                                    if (state.lastkeyframe_rot > state.rot_loopoutframe)
                                        state.lastkeyframe_rot = state.rot_loopinframe;

                                }
                                else
                                {
                                    if (state.nextkeyframe_rot >= ar.anim.joints[jpos].rotationkeys.Length)
                                        state.nextkeyframe_rot = ar.anim.joints[jpos].rotationkeys.Length - 1;

                                    if (state.lastkeyframe_rot >= ar.anim.joints[jpos].rotationkeys.Length)
                                    {
                                        state.lastkeyframe_rot = ar.anim.joints[jpos].rotationkeys.Length - 1;

                                        ar.playstate = animationwrapper.animstate.STATE_EASEOUT;
                                        //animation over
                                    }
                                }
                            }

                            // update the pointers incase they have been moved
                            rot_next = ar.anim.joints[jpos].rotationkeys[state.nextkeyframe_rot];
                            rot_last = ar.anim.joints[jpos].rotationkeys[state.lastkeyframe_rot];

                            // TODO the lerp/delta is faulty
                            // it is not going to handle loop points when we wrap around as last will be > next
                            // it also fails when currenttime < last_time which occurs as keyframe[0] is not exactly at
                            // t(0).
                            float delta = state.currenttime_rot - rot_last.time / (rot_next.time - rot_last.time);
                            delta = Utils.Clamp(delta, 0f, 1f);
                            Vector3 rotlerpv = Vector3.Lerp(rot_last.key_element, rot_next.key_element, delta);
                            // rotlerp = Quaternion.Slerp(Quaternion.Identity,new Quaternion(rotlerpv.X, rotlerpv.Y, rotlerpv.Z), factor);

                            if (easeoutset == false && ar.playstate == animationwrapper.animstate.STATE_EASEOUT)
                            {
                                easeoutset = true;
                                state.easeoutrot = new Quaternion(rotlerpv.X, rotlerpv.Y, rotlerpv.Z);
                                state.easeoutfactor = factor;
                            }
                            else
                            {
                                rotlerp = new Quaternion(rotlerpv.X, rotlerpv.Y, rotlerpv.Z);
                            }
                        }

                        //end of rotation

                        joint jointstate = new joint();

                        if (jointdeforms.TryGetValue(ar.anim.joints[jpos].Name, out jointstate))
                        {
                            jointstate.offset += (poslerp);

                            if (ar.playstate != animationwrapper.animstate.STATE_EASEOUT)
                            {
                                jointstate.rotation = easeoutset 
                                    ? Quaternion.Slerp(jointstate.rotation, state.easeoutrot, state.easeoutfactor) 
                                    : rotlerp;
                            }

                            //jointstate.rotation= Quaternion.Slerp(jointstate.rotation, rotlerp, 0.5f);
                        }
                        else
                        {
                            jointstate = new joint
                            {
                                rotation = rotlerp,
                                offset = poslerp
                            };
                            jointdeforms.Add(ar.anim.joints[jpos].Name, jointstate);
                        }

                        //warning struct copy non reference
                        ar.anim.joints[jpos].Tag = state;

                        jpos++;
                    }
                }
            }

            foreach (var kvp in jointdeforms)
            {
                deformbone(kvp.Key, kvp.Value.offset, kvp.Value.rotation);
            }
            mNeedsMeshRebuild = true;
        }

    }

    public class Bone
    {
        public string name;
        public Vector3 pos;
        public Quaternion rot;
        public Vector3 scale;
        public Vector3 piviot;

        public Vector3 offset_pos;

        public Vector3 orig_pos;
        public Quaternion orig_rot;
        public Vector3 orig_scale;
        public Vector3 orig_piviot;

        Matrix4 mDeformMatrix = Matrix4.Identity;

        public Vector3 animation_offset;

        public Bone parent;

        public List<Bone> children = new List<Bone>();

        public static Dictionary<string, Bone> mBones = new Dictionary<string, Bone>();
        public static Dictionary<int, Bone> mIndexedBones = new Dictionary<int, Bone>();
        static int boneaddindex = 0;

        private bool rotdirty = true;
        private bool posdirty = true;

        //Inverse bind matrix with bone scale
        private Vector3 mTotalPos;

        private Quaternion mTotalRot;

        private Vector3 mDeltaPos;

        public string mParentBone = null;

        public Bone()
        {
        }

        public Bone(Bone source)
        {
            name = source.name;
            pos = new Vector3(source.pos);
            rot = new Quaternion(source.rot);
            scale = new Vector3(source.scale);
            piviot = new Vector3(source.piviot);
            offset_pos = new Vector3(source.offset_pos);

            orig_piviot = source.orig_piviot;
            orig_pos = source.orig_pos;
            orig_rot = source.orig_rot;
            orig_scale = source.orig_scale;

            mParentBone = source.mParentBone;

            mDeformMatrix = new Matrix4(source.mDeformMatrix);
        }

        public static void loadBones(string skeletonfilename)
        {
            lock (mBones) mBones.Clear();
            string basedir = Path.Combine(Directory.GetCurrentDirectory(), "linden", "character");
            XmlDocument skeleton = new XmlDocument();
            skeleton.Load(Path.Combine(basedir, skeletonfilename));
            XmlNode boneslist = skeleton.GetElementsByTagName("linden_skeleton")[0];
            addBone(boneslist.ChildNodes[0], null);
        }

        public static void addBone(XmlNode bone, Bone parent)
        {

            if (bone.Name != "bone")
                return;

            Bone b = new Bone {name = bone.Attributes.GetNamedItem("name").Value};

            string pos = bone.Attributes.GetNamedItem("pos").Value;
            string[] posparts = pos.Split(' ');
            b.pos = new Vector3(float.Parse(posparts[0], Utils.EnUsCulture), float.Parse(posparts[1], Utils.EnUsCulture), float.Parse(posparts[2], Utils.EnUsCulture));
            b.orig_pos = new Vector3(b.pos);
            b.offset_pos = new Vector3(b.pos);

            string rot = bone.Attributes.GetNamedItem("rot").Value;
            string[] rotparts = rot.Split(' ');
            b.rot = Quaternion.CreateFromEulers((float)(float.Parse(rotparts[0], Utils.EnUsCulture) * Math.PI / 180f), (float)(float.Parse(rotparts[1], Utils.EnUsCulture) * Math.PI / 180f), (float)(float.Parse(rotparts[2], Utils.EnUsCulture) * Math.PI / 180f));
            b.orig_rot = new Quaternion(b.rot);

            string scale = bone.Attributes.GetNamedItem("scale").Value;
            string[] scaleparts = scale.Split(' ');
            b.scale = new Vector3(float.Parse(scaleparts[0], Utils.EnUsCulture), float.Parse(scaleparts[1], Utils.EnUsCulture), float.Parse(scaleparts[2], Utils.EnUsCulture));
            b.orig_scale = new Vector3(b.scale);


            float[] deform = Math3D.CreateSRTMatrix(new Vector3(1, 1, 1), b.rot, b.orig_pos);
            b.mDeformMatrix = new Matrix4(deform[0], deform[1], deform[2], deform[3], deform[4], deform[5], deform[6], deform[7], deform[8], deform[9], deform[10], deform[11], deform[12], deform[13], deform[14], deform[15]);

            //TODO piviot

            b.parent = parent;
            if (parent != null)
            {
                b.mParentBone = parent.name;
                parent.children.Add(b);
            }

            lock (mBones) mBones.Add(b.name, b);
            mIndexedBones.Add(boneaddindex++, b);

            Logger.Log($"Found bone {b.name}", Helpers.LogLevel.Debug);

            foreach (XmlNode childbone in bone.ChildNodes)
            {
                addBone(childbone, b);
            }

        }

        public void deformbone(Vector3 dpos, Quaternion rot)
        {
            //float[] deform = Math3D.CreateSRTMatrix(scale, rot, this.orig_pos);
            //mDeformMatrix = new Matrix4(deform[0], deform[1], deform[2], deform[3], deform[4], deform[5], deform[6], deform[7], deform[8], deform[9], deform[10], deform[11], deform[12], deform[13], deform[14], deform[15]);

            //this.offset_pos += pos;
            //this.pos = pos;
            // this.offset_pos = pos;

            animation_offset = dpos;

            //this.pos = Bone.mBones[name].offset_pos + dpos;
            lock (mBones) this.rot = mBones[name].orig_rot * rot;

            markdirty();
        }

        public void scalebone(Vector3 scale)
        {
            this.scale *= scale;
            markdirty();
        }

        public void offsetbone(Vector3 offset)
        {
            offset_pos += offset;
            markdirty();
        }

        // If we deform a bone mark this bone and all its children as dirty.  
        public void markdirty()
        {
            rotdirty = true;
            posdirty = true;
            foreach (Bone childbone in children)
            {
                childbone.markdirty();
            }
        }

        public Matrix4 getdeform()
        {
            if (parent != null)
            {
                return mDeformMatrix * parent.getdeform();
            }
            else
            {
                return mDeformMatrix;
            }
        }

        private Vector3 getOffset()
        {
            if (parent != null)
            {
                Quaternion totalrot = getParentRot(); // we don't want this joints rotation included
                Vector3 parento = parent.getOffset();
                mTotalPos = parento + pos * parent.scale * totalrot;
                Vector3 orig = getOrigOffset();
                mDeltaPos = mTotalPos - orig;

                posdirty = false;

                return mTotalPos;
            }
            else
            {
                Vector3 orig = getOrigOffset();
                //mTotalPos = (pos * scale)+offset_pos;
                //mTotalPos = (pos) + offset_pos;
                mTotalPos = pos;
                mDeltaPos = mTotalPos - orig;
                posdirty = false;
                return mTotalPos;

            }
        }

        public Vector3 getMyOffset()
        {
            return pos * scale;
        }

        // Try to save some cycles by not recalculating positions and rotations every time
        public Vector3 getTotalOffset()
        {
            return posdirty == false ? mTotalPos : getOffset();
        }

        public Vector3 getDeltaOffset()
        {
            if (posdirty == false)
            {
                return mDeltaPos;
            }
            getOffset();
            return mDeltaPos;
        }

        private Vector3 getOrigOffset()
        {
            if (parent != null)
            {
                return (parent.getOrigOffset() + orig_pos);
            }
            else
            {
                return orig_pos;
            }
        }

        private static Quaternion getRotation(string bonename)
        {
            Bone b;
            lock (mBones)
            {
                return mBones.TryGetValue(bonename, out b) ? (b.getRotation()) : Quaternion.Identity;
            }
        }


        private Quaternion getParentRot()
        {
            Quaternion totalrot = Quaternion.Identity;

            if (parent != null)
            {
                totalrot = parent.getRotation();
            }

            return totalrot;

        }

        private Quaternion getRotation()
        {
            Quaternion totalrot = rot;

            if (parent != null)
            {
                totalrot = parent.getRotation() * rot;
            }

            mTotalRot = totalrot;
            rotdirty = false;

            return totalrot;
        }

        public Quaternion getTotalRotation()
        {
            return rotdirty == false ? mTotalRot : getRotation();
        }
    }

    public class BoneDeform
    {
        public BoneDeform(Vector3 scale, Vector3 offset)
        {
            this.scale = scale;
            this.offset = offset;
        }

        public Vector3 scale;
        public Vector3 offset;
    }

    public class VisualParamEx
    {
        //All visual params indexed by ID
        public static Dictionary<int, VisualParamEx> AllParams = new Dictionary<int, VisualParamEx>();

        // The sorted list of tweakable params, this matches the AvatarAppearance packet visual
        // parameters ordering
        public static SortedList TweakableParams = new SortedList();

        public Dictionary<string, BoneDeform> BoneDeforms = null;

        public Dictionary<string, VolumeDeform> VolumeDeforms = null;

        public List<driven> ChildParams = null;

        public List<VisualParamEx> IdenticalIds = new List<VisualParamEx>();

        public string MorphMesh = null;

        enum GroupType
        {
            VISUAL_PARAM_GROUP_TWEAKABLE = 0,
            VISUAL_PARAM_GROUP_ANIMATABLE,
            VISUAL_PARAM_GROUP_TWEAKABLE_NO_TRANSMIT,
            VISUAL_PARAM_GROUP_TRANSMIT_NOT_TWEAKABLE
        }

        public struct VolumeDeform
        {
            public string name;
            public Vector3 scale;
            public Vector3 pos;
        }

        public enum EparamSex
        {
            SEX_MALE = 0x1,
            SEX_FEMALE = 0x2,
            SEX_BOTH = 0x3
        }

        public enum ParamType
        {
            TYPE_NOTSET = 0,
            TYPE_BONEDEFORM,
            TYPE_MORPH,
            TYPE_DRIVER,
            TYPE_COLOR,
            TYPE_LAYER
        }

        public struct driven
        {
            public int id;
            public float max1;
            public float max2;
            public float min1;
            public float min2;
            public bool hasMinMax;
        }

        public string meshname;

        /// <summary>Index of this visual param</summary>
        public int ParamID;
        /// <summary>Internal name</summary>
        public string Name;
        /// <summary>Group ID this parameter belongs to</summary>
        public int Group;
        /// <summary>Name of the wearable this parameter belongs to</summary>
        public string Wearable;
        /// <summary>Displayable label of this characteristic</summary>
        public string Label;
        /// <summary>Displayable label for the minimum value of this characteristic</summary>
        public string LabelMin;
        /// <summary>Displayable label for the maximum value of this characteristic</summary>
        public string LabelMax;
        /// <summary>Default value</summary>
        public float DefaultValue;
        /// <summary>Minimum value</summary>
        public float MinValue;
        /// <summary>Maximum value</summary>
        public float MaxValue;
        /// <summary>Is this param used for creation of bump layer?</summary>
        public bool IsBumpAttribute;
        /// <summary>Alpha blending/bump info</summary>
        public VisualAlphaParam? AlphaParams;
        /// <summary>Color information</summary>
        public VisualColorParam? ColorParams;
        /// <summary>Array of param IDs that are drivers for this parameter</summary>
        public int[] Drivers;
        /// <summary>The Avatar Sex that this parameter applies to</summary>
        public EparamSex sex;

        public ParamType pType;

        public static int count = 0;

        /// <summary>
        /// Set all the values through the constructor
        /// </summary>
        /// <param name="paramID">Index of this visual param</param>
        /// <param name="name">Internal name</param>
        /// <param name="group"></param>
        /// <param name="wearable"></param>
        /// <param name="label">Displayable label of this characteristic</param>
        /// <param name="labelMin">Displayable label for the minimum value of this characteristic</param>
        /// <param name="labelMax">Displayable label for the maximum value of this characteristic</param>
        /// <param name="def">Default value</param>
        /// <param name="min">Minimum value</param>
        /// <param name="max">Maximum value</param>
        /// <param name="isBumpAttribute">Is this param used for creation of bump layer?</param>
        /// <param name="drivers">Array of param IDs that are drivers for this parameter</param>
        /// <param name="alpha">Alpha blending/bump info</param>
        /// <param name="colorParams">Color information</param>
        public VisualParamEx(int paramID, string name, int group, string wearable, string label, string labelMin, string labelMax,
            float def, float min, float max, bool isBumpAttribute, int[] drivers, VisualAlphaParam? alpha, VisualColorParam? colorParams)
        {
            ParamID = paramID;
            Name = name;
            Group = group;
            Wearable = wearable;
            Label = label;
            LabelMin = labelMin;
            LabelMax = labelMax;
            DefaultValue = def;
            MaxValue = max;
            MinValue = min;
            IsBumpAttribute = isBumpAttribute;
            Drivers = drivers;
            AlphaParams = alpha;
            ColorParams = colorParams;
            sex = EparamSex.SEX_BOTH;
        }

        public bool matchchildnode(string test, XmlNode node)
        {
            return node.ChildNodes.Cast<XmlNode>().Any(n => n.Name == test);
        }

        public VisualParamEx(XmlNode node)
        {

            ParamID = Int32.Parse(node.Attributes.GetNamedItem("id").Value);
            Name = node.Attributes.GetNamedItem("name").Value;
            Group = Int32.Parse(node.Attributes.GetNamedItem("group").Value);

            //These dont exist for facal expresion morphs
            if (node.Attributes.GetNamedItem("wearable") != null)
                Wearable = node.Attributes.GetNamedItem("wearable").Value;

            MinValue = float.Parse(node.Attributes.GetNamedItem("value_min").Value, Utils.EnUsCulture);
            MaxValue = float.Parse(node.Attributes.GetNamedItem("value_max").Value, Utils.EnUsCulture);

            // These do not exists for driven parameters
            if (node.Attributes.GetNamedItem("label_min") != null)
            {
                LabelMin = node.Attributes.GetNamedItem("label_min").Value;
            }

            if (node.Attributes.GetNamedItem("label_max") != null)
            {
                LabelMax = node.Attributes.GetNamedItem("label_max").Value;
            }

            XmlNode sexnode = node.Attributes.GetNamedItem("sex");

            if (sexnode != null)
            {
                switch (sexnode.Value)
                {
                    case "male":
                        sex = EparamSex.SEX_MALE;
                        break;
                    case "female":
                        sex = EparamSex.SEX_FEMALE;
                        break;
                    default:
                        sex = EparamSex.SEX_BOTH;
                        break;
                }
            }

            if (node.ParentNode.Name == "mesh")
            {
                MorphMesh = node.ParentNode.Attributes.GetNamedItem("type").Value;
            }

            if (Group == (int)GroupType.VISUAL_PARAM_GROUP_TWEAKABLE)
            {
                if (!TweakableParams.ContainsKey(ParamID)) //stupid duplicate shared params
                {
                    TweakableParams.Add(ParamID, this);
                }
                else
                {
                    Logger.Log($"Warning duplicate tweakable parameter ID {count} {Name}", Helpers.LogLevel.Warning);
                }
                count++;
            }

            if (AllParams.ContainsKey(ParamID))
            {
                //Logger.Log("Shared VisualParam id " + ParamID.ToString() + " "+Name, Helpers.LogLevel.Info);
                AllParams[ParamID].IdenticalIds.Add(this);
            }
            else
            {
                //Logger.Log("VisualParam id " + ParamID.ToString() + " " + Name, Helpers.LogLevel.Info);
                AllParams.Add(ParamID, this);
            }

            if (matchchildnode("param_skeleton", node))
            {
                pType = ParamType.TYPE_BONEDEFORM;
                // If we are in the skeleton section then we also have bone deforms to parse
                BoneDeforms = new Dictionary<string, BoneDeform>();
                if (node.HasChildNodes && node.ChildNodes[0].HasChildNodes)
                {
                    ParseBoneDeforms(node.ChildNodes[0].ChildNodes);
                }
            }

            if (matchchildnode("param_morph", node))
            {
                pType = ParamType.TYPE_MORPH;

                VolumeDeforms = new Dictionary<string, VolumeDeform>();
                if (node.HasChildNodes && node.ChildNodes[0].HasChildNodes)
                {
                    ParseVolumeDeforms(node.ChildNodes[0].ChildNodes);
                }
            }

            if (matchchildnode("param_driver", node))
            {
                pType = ParamType.TYPE_DRIVER;
                ChildParams = new List<driven>();
                if (node.HasChildNodes && node.ChildNodes[0].HasChildNodes) //LAZY
                {
                    ParseDrivers(node.ChildNodes[0].ChildNodes);
                }
            }

            if (matchchildnode("param_color", node))
            {
                pType = ParamType.TYPE_COLOR;
                if (node.HasChildNodes)
                {
                    foreach (XmlNode colorchild in node.ChildNodes)
                    {
                        if (colorchild.Name == "param_color")
                        {
                            //TODO extract <value color="50, 25, 5, 255" />
                        }
                    }
                }
            }

        }

        void ParseBoneDeforms(XmlNodeList deforms)
        {
            foreach (XmlNode node in deforms)
            {
                if (node.Name == "bone")
                {
                    string name = node.Attributes.GetNamedItem("name").Value;
                    Vector3 scale = Vector3.One;
                    Vector3 offset = Vector3.One;

                    if (node.Attributes.GetNamedItem("scale") != null)
                        scale = XmlParseVector(node.Attributes.GetNamedItem("scale").Value);

                    if (node.Attributes.GetNamedItem("offset") != null)
                        offset = XmlParseVector(node.Attributes.GetNamedItem("offset").Value);

                    BoneDeform bd = new BoneDeform(scale, offset);

                    BoneDeforms.Add(name, bd);
                }
            }
        }

        void ParseVolumeDeforms(XmlNodeList deforms)
        {
            foreach (XmlNode node in deforms)
            {
                if (node.Name == "volume_morph")
                {
                    VolumeDeform vd = new VolumeDeform
                    {
                        name = node.Attributes.GetNamedItem("name").Value.ToLower(),
                        scale =
                            node.Attributes.GetNamedItem("scale") != null
                                ? XmlParseVector(node.Attributes.GetNamedItem("scale").Value)
                                : new Vector3(0, 0, 0),
                        pos =
                            node.Attributes.GetNamedItem("pos") != null
                                ? XmlParseVector(node.Attributes.GetNamedItem("pos").Value)
                                : new Vector3(0f, 0f, 0f)
                    };

                    VolumeDeforms.Add(vd.name, vd);
                }
            }
        }

        void ParseDrivers(XmlNodeList drivennodes)
        {
            foreach (XmlNode node in drivennodes)
            {
                if (node.Name == "driven")
                {
                    driven d = new driven {id = Int32.Parse(node.Attributes.GetNamedItem("id").Value)};

                    XmlNode param = node.Attributes.GetNamedItem("max1");
                    if (param != null)
                    {
                        d.max1 = float.Parse(param.Value, Utils.EnUsCulture);
                        d.max2 = float.Parse(node.Attributes.GetNamedItem("max2").Value, Utils.EnUsCulture);
                        d.min1 = float.Parse(node.Attributes.GetNamedItem("min1").Value, Utils.EnUsCulture);
                        d.min2 = float.Parse(node.Attributes.GetNamedItem("min2").Value, Utils.EnUsCulture);
                        d.hasMinMax = true;
                    }
                    else
                    {
                        d.hasMinMax = false;
                    }

                    ChildParams.Add(d);

                }
            }
        }

        public static Vector3 XmlParseVector(string data)
        {
            string[] posparts = data.Split(Array.Empty<char>(), StringSplitOptions.RemoveEmptyEntries);
            return new Vector3(float.Parse(posparts[0], Utils.EnUsCulture), float.Parse(posparts[1], Utils.EnUsCulture), float.Parse(posparts[2], Utils.EnUsCulture));
        }

        public static Quaternion XmlParseRotation(string data)
        {
            string[] rotparts = data.Split(' ');
            return Quaternion.CreateFromEulers((float)(float.Parse(rotparts[0]) * Math.PI / 180f), (float)(float.Parse(rotparts[1]) * Math.PI / 180f), (float)(float.Parse(rotparts[2]) * Math.PI / 180f));
        }
    }

    public class RenderAvatar : SceneObject
    {
        public static Dictionary<int, string> BakedTextures = new Dictionary<int, string>
            {
                { 8, "head" },
                { 9, "upper" },
                { 10, "lower" },
                { 11, "eyes" },
                { 19, "skirt" },
                { 20, "hair" }
            };

        public GLAvatar glavatar = new GLAvatar();
        public Avatar avatar;
        public FaceData[] data = new FaceData[32];
        public Dictionary<UUID, Animation> animlist = new Dictionary<UUID, Animation>();
        public Dictionary<WearableType, AppearanceManager.WearableData> Wearables = new Dictionary<WearableType, AppearanceManager.WearableData>();
        public static readonly BoundingVolume AvatarBoundingVolume;

        // Static constructor
        static RenderAvatar()
        {
            AvatarBoundingVolume = new BoundingVolume();
            AvatarBoundingVolume.FromScale(Vector3.One);
        }

        // Default constructor
        public RenderAvatar()
        {
            BoundingVolume = AvatarBoundingVolume;
            Type = SceneObjectType.Avatar;
        }

        public override Primitive BasePrim
        {
            get => avatar;
            set
            {
                if (value is Avatar)
                {
                    avatar = (Avatar)value;
                    AvatarBoundingVolume.CalcScaled(avatar.Scale);
                }
            }
        }

        public override void Step(float time)
        {
            glavatar.skel.animate(time);
            base.Step(time);
        }

        public float Height;
        public float PelvisToFoot;

        public void UpdateSize()
        {
            float F_SQRT2 = 1.4142135623730950488016887242097f;

            Vector3 pelvis_scale = glavatar.skel.mBones["mPelvis"].scale;

            Vector3 skull = glavatar.skel.mBones["mSkull"].pos;
            Vector3 skull_scale = glavatar.skel.mBones["mSkull"].scale;

            Vector3 neck = glavatar.skel.mBones["mNeck"].pos;
            Vector3 neck_scale = glavatar.skel.mBones["mNeck"].scale;

            Vector3 chest = glavatar.skel.mBones["mChest"].pos;
            Vector3 chest_scale = glavatar.skel.mBones["mChest"].scale;

            Vector3 head = glavatar.skel.mBones["mHead"].pos;
            Vector3 head_scale = glavatar.skel.mBones["mHead"].scale;

            Vector3 torso = glavatar.skel.mBones["mTorso"].pos;
            Vector3 torso_scale = glavatar.skel.mBones["mTorso"].scale;

            Vector3 hip = glavatar.skel.mBones["mHipLeft"].pos;
            Vector3 hip_scale = glavatar.skel.mBones["mHipLeft"].scale;

            Vector3 knee = glavatar.skel.mBones["mKneeLeft"].pos;
            Vector3 knee_scale = glavatar.skel.mBones["mKneeLeft"].scale;

            Vector3 ankle = glavatar.skel.mBones["mAnkleLeft"].pos;
            Vector3 ankle_scale = glavatar.skel.mBones["mAnkleLeft"].scale;

            Vector3 foot = glavatar.skel.mBones["mFootLeft"].pos;

            // mAvatarOffset.Z = getVisualParamWeight(11001);

            float mPelvisToFoot = hip.Z * pelvis_scale.Z -
                            knee.Z * hip_scale.Z -
                            ankle.Z * knee_scale.Z -
                            foot.Z * ankle_scale.Z;

            Vector3 new_body_size;
            new_body_size.Z = mPelvisToFoot +
                // the sqrt(2) correction below is an approximate
                // correction to get to the top of the head
                               F_SQRT2 * (skull.Z * head_scale.Z) +
                               head.Z * neck_scale.Z +
                               neck.Z * chest_scale.Z +
                               chest.Z * torso_scale.Z +
                               torso.Z * pelvis_scale.Z;

            Height = new_body_size.Z;
            PelvisToFoot = mPelvisToFoot;
        }

        public Vector3 AdjustedPosition(Vector3 source)
        {
            return new Vector3(source.X, source.Y, source.Z - Height + PelvisToFoot);
        }
    }
}
