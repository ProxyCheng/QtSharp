using System;
using System.Linq;

// Every binding module this generator knows, in one table: the ten base modules of QtSharp.Api
// and the addon families (Qt Charts, Qt3D). A module is data - name, assembly, package, Qt modules,
// dependencies, native library, roots - and the generator, the build scripts, the project
// generator and the package projects all read this one registry. There are no per-addon branches
// anywhere in the generator: adding a Qt module is a row here plus its model and projects.
//
// Base modules take their classes from the checked-in combined model (grouped by header); addon
// modules list their Roots explicitly, because those roots seed their own extraction.
public static partial class ApiModel
{
    static ExtensionModule CoreModule(string name, string ns, string qtModule,
                                      string[] dependsOn, string[] libs, string[] cppNamespaces)
        => new ExtensionModule
        {
            Name = name,
            Namespace = ns,
            Assembly = ns,
            Package = "QtSharp.Api",
            IsCore = true,
            // The base modules share the one native glue generated from the combined model.
            NativeLibrary = "QtSharp.Api",
            QtModules = new[] { qtModule },
            QtDefines = Array.Empty<string>(),
            QtLibs = libs,
            DependsOn = dependsOn,
            CppNamespaces = cppNamespaces,
            Roots = Roots.Where(r => r.Module == qtModule).ToArray(),
        };

    static ExtensionModule QtCoreModule()
        => CoreModule("qtcore", "QtSharp.QtCore", "QtCore",
            Array.Empty<string>(), new[] { "Qt6Core" }, new[] { "Qt" });

    static ExtensionModule QtGuiModule()
        => CoreModule("qtgui", "QtSharp.QtGui", "QtGui",
            new[] { "qtcore" }, new[] { "Qt6Gui", "Qt6Core" }, Array.Empty<string>());

    static ExtensionModule QtWidgetsModule()
        => CoreModule("qtwidgets", "QtSharp.QtWidgets", "QtWidgets",
            new[] { "qtgui", "qtcore" }, new[] { "Qt6Widgets", "Qt6Gui", "Qt6Core" }, Array.Empty<string>());

    static ExtensionModule QtNetworkModule()
        => CoreModule("qtnetwork", "QtSharp.QtNetwork", "QtNetwork",
            new[] { "qtcore" }, new[] { "Qt6Network", "Qt6Core" }, Array.Empty<string>());

    static ExtensionModule QtSvgModule()
        => CoreModule("qtsvg", "QtSharp.QtSvg", "QtSvg",
            new[] { "qtgui", "qtcore" }, new[] { "Qt6Svg", "Qt6Gui", "Qt6Core" }, Array.Empty<string>());

    static ExtensionModule QtSvgWidgetsModule()
        => CoreModule("qtsvgwidgets", "QtSharp.QtSvgWidgets", "QtSvgWidgets",
            new[] { "qtsvg", "qtwidgets", "qtgui", "qtcore" },
            new[] { "Qt6SvgWidgets", "Qt6Svg", "Qt6Widgets", "Qt6Gui", "Qt6Core" }, Array.Empty<string>());

    static ExtensionModule QtPrintSupportModule()
        => CoreModule("qtprintsupport", "QtSharp.QtPrintSupport", "QtPrintSupport",
            new[] { "qtwidgets", "qtgui", "qtcore" },
            new[] { "Qt6PrintSupport", "Qt6Widgets", "Qt6Gui", "Qt6Core" }, Array.Empty<string>());

    static ExtensionModule QtXmlModule()
        => CoreModule("qtxml", "QtSharp.QtXml", "QtXml",
            new[] { "qtcore" }, new[] { "Qt6Xml", "Qt6Core" }, Array.Empty<string>());

    static ExtensionModule QtOpenGLModule()
        => CoreModule("qtopengl", "QtSharp.QtOpenGL", "QtOpenGL",
            new[] { "qtgui", "qtcore" }, new[] { "Qt6OpenGL", "Qt6Gui", "Qt6Core" }, Array.Empty<string>());

    static ExtensionModule QtOpenGLWidgetsModule()
        => CoreModule("qtopenglwidgets", "QtSharp.QtOpenGLWidgets", "QtOpenGLWidgets",
            new[] { "qtopengl", "qtwidgets", "qtgui", "qtcore" },
            new[] { "Qt6OpenGLWidgets", "Qt6OpenGL", "Qt6Widgets", "Qt6Gui", "Qt6Core" }, Array.Empty<string>());

    static ExtensionModule QtPositioningModule() => new ExtensionModule
    {
        Name = "qtpositioning",
        Namespace = "QtSharp.QtPositioning",
        Assembly = "QtSharp.QtPositioning",
        Package = "QtSharp.Positioning",
        NativeLibrary = "QtSharp.QtPositioning.Native",
        CppNamespaces = Array.Empty<string>(),
        QtModules = new[] { "QtPositioning" },
        QtDefines = Array.Empty<string>(),
        QtLibs = new[] { "Qt6Positioning" },
        DependsOn = new[] { "qtcore" },
        Roots = new[]
        {
            R("QGeoAddress", "qgeoaddress.h", "QtPositioning"),
            R("QGeoAreaMonitorInfo", "qgeoareamonitorinfo.h", "QtPositioning"),
            R("QGeoAreaMonitorSource", "qgeoareamonitorsource.h", "QtPositioning", bindCtors: false),
            R("QGeoCircle", "qgeocircle.h", "QtPositioning"),
            R("QGeoCoordinate", "qgeocoordinate.h", "QtPositioning"),
            R("QGeoLocation", "qgeolocation.h", "QtPositioning"),
            R("QGeoPath", "qgeopath.h", "QtPositioning"),
            R("QGeoPolygon", "qgeopolygon.h", "QtPositioning"),
            R("QGeoPositionInfo", "qgeopositioninfo.h", "QtPositioning"),
            R("QGeoPositionInfoSource", "qgeopositioninfosource.h", "QtPositioning", bindCtors: false),
            R("QGeoRectangle", "qgeorectangle.h", "QtPositioning"),
            R("QGeoSatelliteInfo", "qgeosatelliteinfo.h", "QtPositioning"),
            R("QGeoSatelliteInfoSource", "qgeosatelliteinfosource.h", "QtPositioning", bindCtors: false),
            R("QGeoShape", "qgeoshape.h", "QtPositioning"),
            R("QNmeaPositionInfoSource", "qnmeapositioninfosource.h", "QtPositioning"),
            R("QNmeaSatelliteInfoSource", "qnmeasatelliteinfosource.h", "QtPositioning"),
        },
    };

    static ExtensionModule QtMultimediaModule() => new ExtensionModule
    {
        Name = "qtmultimedia",
        Namespace = "QtSharp.QtMultimedia",
        Assembly = "QtSharp.QtMultimedia",
        Package = "QtSharp.Multimedia",
        NativeLibrary = "QtSharp.QtMultimedia.Native",
        CppNamespaces = Array.Empty<string>(),
        // The widget half (QVideoWidget, QGraphicsVideoItem) lives in its own Qt include dir, but
        // it is the same package and the same assembly, so both dirs are listed here.
        QtModules = new[] { "QtMultimedia", "QtMultimediaWidgets" },
        QtDefines = Array.Empty<string>(),
        QtLibs = new[] { "Qt6Multimedia", "Qt6MultimediaWidgets", "Qt6Network" },
        DependsOn = new[] { "qtcore", "qtgui", "qtwidgets", "qtnetwork" },
        Roots = new[]
        {
            R("QAudioBuffer", "qaudiobuffer.h", "QtMultimedia"),
            R("QAudioDecoder", "qaudiodecoder.h", "QtMultimedia"),
            R("QAudioDevice", "qaudiodevice.h", "QtMultimedia"),
            R("QAudioFormat", "qaudioformat.h", "QtMultimedia"),
            R("QAudioInput", "qaudioinput.h", "QtMultimedia"),
            R("QAudioOutput", "qaudiooutput.h", "QtMultimedia"),
            R("QAudioSink", "qaudiosink.h", "QtMultimedia"),
            R("QAudioSource", "qaudiosource.h", "QtMultimedia"),
            R("QCamera", "qcamera.h", "QtMultimedia"),
            R("QCameraDevice", "qcameradevice.h", "QtMultimedia"),
            R("QCameraFormat", "qcameradevice.h", "QtMultimedia"),
            R("QCapturableWindow", "qcapturablewindow.h", "QtMultimedia"),
            R("QImageCapture", "qimagecapture.h", "QtMultimedia"),
            R("QMediaCaptureSession", "qmediacapturesession.h", "QtMultimedia"),
            R("QMediaDevices", "qmediadevices.h", "QtMultimedia"),
            R("QMediaFormat", "qmediaformat.h", "QtMultimedia"),
            R("QMediaMetaData", "qmediametadata.h", "QtMultimedia"),
            R("QMediaPlayer", "qmediaplayer.h", "QtMultimedia"),
            R("QMediaRecorder", "qmediarecorder.h", "QtMultimedia"),
            R("QMediaTimeRange", "qmediatimerange.h", "QtMultimedia"),
            R("QPlaybackOptions", "qplaybackoptions.h", "QtMultimedia"),
            R("QScreenCapture", "qscreencapture.h", "QtMultimedia"),
            R("QSoundEffect", "qsoundeffect.h", "QtMultimedia"),
            R("QVideoFrame", "qvideoframe.h", "QtMultimedia"),
            R("QVideoFrameFormat", "qvideoframeformat.h", "QtMultimedia"),
            R("QVideoSink", "qvideosink.h", "QtMultimedia"),
            R("QWindowCapture", "qwindowcapture.h", "QtMultimedia"),
            R("QGraphicsVideoItem", "qgraphicsvideoitem.h", "QtMultimediaWidgets"),
            R("QVideoWidget", "qvideowidget.h", "QtMultimediaWidgets"),
        },
    };

    static ExtensionModule Qt3DCoreModule() => new ExtensionModule
    {
        Name = "qt3dcore",
        Namespace = "QtSharp.Qt3DCore",
        // Qt3DCore is a QtGui/QtCore addon: every class derives from QObject.
        DependsOn = new[] { "qtcore", "qtgui" },
        Assembly = "QtSharp.Qt3DCore",
        Package = "QtSharp.Qt3D",
        NativeLibrary = "QtSharp.Qt3DCore.Native",
        CppNamespaces = new[] { "Qt3DCore" },
        QtModules = new[] { "Qt3DCore" },
        QtDefines = new[] { "QT3DCORE_LIB" },
        QtLibs = new[] { "Qt63DCore" },
        Roots = new[]
        {
            R("Qt3DCore::QAbstractAspect", "qabstractaspect.h", "Qt3DCore"),
            R("Qt3DCore::QAbstractFunctor", "qabstractfunctor.h", "Qt3DCore"),
            R("Qt3DCore::QAbstractSkeleton", "qabstractskeleton.h", "Qt3DCore"),
            R("Qt3DCore::QArmature", "qarmature.h", "Qt3DCore"),
            R("Qt3DCore::QAspectEngine", "qaspectengine.h", "Qt3DCore"),
            R("Qt3DCore::QAttribute", "qattribute.h", "Qt3DCore"),
            R("Qt3DCore::QBoundingVolume", "qboundingvolume.h", "Qt3DCore"),
            R("Qt3DCore::QBuffer", "qbuffer.h", "Qt3DCore"),
            R("Qt3DCore::QComponent", "qcomponent.h", "Qt3DCore"),
            R("Qt3DCore::QCoreAspect", "qcoreaspect.h", "Qt3DCore"),
            R("Qt3DCore::QCoreSettings", "qcoresettings.h", "Qt3DCore"),
            R("Qt3DCore::QEntity", "qentity.h", "Qt3DCore"),
            R("Qt3DCore::QGeometry", "qgeometry.h", "Qt3DCore"),
            R("Qt3DCore::QGeometryView", "qgeometryview.h", "Qt3DCore"),
            R("Qt3DCore::QJoint", "qjoint.h", "Qt3DCore"),
            R("Qt3DCore::QNode", "qnode.h", "Qt3DCore"),
            R("Qt3DCore::QNodeId", "qnodeid.h", "Qt3DCore"),
            R("Qt3DCore::QSkeleton", "qskeleton.h", "Qt3DCore"),
            R("Qt3DCore::QSkeletonLoader", "qskeletonloader.h", "Qt3DCore"),
            R("Qt3DCore::QTransform", "qtransform.h", "Qt3DCore"),
        },
    };

    static ExtensionModule Qt3DRenderModule() => new ExtensionModule
    {
        Name = "qt3drender",
        Namespace = "QtSharp.Qt3DRender",
        Assembly = "QtSharp.Qt3DRender",
        Package = "QtSharp.Qt3D",
        NativeLibrary = "QtSharp.Qt3DRender.Native",
        CppNamespaces = new[] { "Qt3DRender" },
        QtModules = new[] { "Qt3DRender" },
        QtDefines = new[] { "QT3DRENDER_LIB" },
        QtLibs = new[] { "Qt63DRender", "Qt63DCore" },
        DependsOn = new[] {"qtcore", "qtgui", "qtopengl", "qt3dcore"},
        // qrenderapi.h declares Qt3DRender::API; no Qt3DRender root includes it, but Qt3DExtras'
        // Qt3DWindow constructor takes it, so the module has to publish it.
        ExtraHeaders = new[] { "qrenderapi.h" },
        Roots = new[]
        {
            R("Qt3DRender::QAbstractLight", "qabstractlight.h", "Qt3DRender"),
            R("Qt3DRender::QAbstractRayCaster", "qabstractraycaster.h", "Qt3DRender"),
            R("Qt3DRender::QAbstractTexture", "qabstracttexture.h", "Qt3DRender"),
            R("Qt3DRender::QAbstractTextureImage", "qabstracttextureimage.h", "Qt3DRender"),
            R("Qt3DRender::QAlphaCoverage", "qalphacoverage.h", "Qt3DRender"),
            R("Qt3DRender::QAlphaTest", "qalphatest.h", "Qt3DRender"),
            R("Qt3DRender::QBlendEquation", "qblendequation.h", "Qt3DRender"),
            R("Qt3DRender::QBlendEquationArguments", "qblendequationarguments.h", "Qt3DRender"),
            R("Qt3DRender::QBlitFramebuffer", "qblitframebuffer.h", "Qt3DRender"),
            R("Qt3DRender::QBufferCapture", "qbuffercapture.h", "Qt3DRender"),
            R("Qt3DRender::QCamera", "qcamera.h", "Qt3DRender"),
            R("Qt3DRender::QCameraLens", "qcameralens.h", "Qt3DRender"),
            R("Qt3DRender::QCameraSelector", "qcameraselector.h", "Qt3DRender"),
            R("Qt3DRender::QClearBuffers", "qclearbuffers.h", "Qt3DRender"),
            R("Qt3DRender::QClipPlane", "qclipplane.h", "Qt3DRender"),
            R("Qt3DRender::QColorMask", "qcolormask.h", "Qt3DRender"),
            R("Qt3DRender::QComputeCommand", "qcomputecommand.h", "Qt3DRender"),
            R("Qt3DRender::QCullFace", "qcullface.h", "Qt3DRender"),
            R("Qt3DRender::QDebugOverlay", "qdebugoverlay.h", "Qt3DRender"),
            R("Qt3DRender::QDepthRange", "qdepthrange.h", "Qt3DRender"),
            R("Qt3DRender::QDepthTest", "qdepthtest.h", "Qt3DRender"),
            R("Qt3DRender::QDirectionalLight", "qdirectionallight.h", "Qt3DRender"),
            R("Qt3DRender::QDispatchCompute", "qdispatchcompute.h", "Qt3DRender"),
            R("Qt3DRender::QDithering", "qdithering.h", "Qt3DRender"),
            R("Qt3DRender::QEffect", "qeffect.h", "Qt3DRender"),
            R("Qt3DRender::QEnvironmentLight", "qenvironmentlight.h", "Qt3DRender"),
            R("Qt3DRender::QFilterKey", "qfilterkey.h", "Qt3DRender"),
            R("Qt3DRender::QFrameGraphNode", "qframegraphnode.h", "Qt3DRender"),
            R("Qt3DRender::QFrontFace", "qfrontface.h", "Qt3DRender"),
            R("Qt3DRender::QFrustumCulling", "qfrustumculling.h", "Qt3DRender"),
            R("Qt3DRender::QGeometryRenderer", "qgeometryrenderer.h", "Qt3DRender"),
            R("Qt3DRender::QGraphicsApiFilter", "qgraphicsapifilter.h", "Qt3DRender"),
            R("Qt3DRender::QLayer", "qlayer.h", "Qt3DRender"),
            R("Qt3DRender::QLayerFilter", "qlayerfilter.h", "Qt3DRender"),
            R("Qt3DRender::QLevelOfDetail", "qlevelofdetail.h", "Qt3DRender"),
            R("Qt3DRender::QLevelOfDetailBoundingSphere", "qlevelofdetailboundingsphere.h", "Qt3DRender"),
            R("Qt3DRender::QLevelOfDetailSwitch", "qlevelofdetailswitch.h", "Qt3DRender"),
            R("Qt3DRender::QLineWidth", "qlinewidth.h", "Qt3DRender"),
            R("Qt3DRender::QMaterial", "qmaterial.h", "Qt3DRender"),
            R("Qt3DRender::QMemoryBarrier", "qmemorybarrier.h", "Qt3DRender"),
            R("Qt3DRender::QMesh", "qmesh.h", "Qt3DRender"),
            R("Qt3DRender::QMultiSampleAntiAliasing", "qmultisampleantialiasing.h", "Qt3DRender"),
            R("Qt3DRender::QNoDepthMask", "qnodepthmask.h", "Qt3DRender"),
            R("Qt3DRender::QNoDraw", "qnodraw.h", "Qt3DRender"),
            R("Qt3DRender::QNoPicking", "qnopicking.h", "Qt3DRender"),
            R("Qt3DRender::QObjectPicker", "qobjectpicker.h", "Qt3DRender"),
            R("Qt3DRender::QPaintedTextureImage", "qpaintedtextureimage.h", "Qt3DRender"),
            R("Qt3DRender::QParameter", "qparameter.h", "Qt3DRender"),
            R("Qt3DRender::QPickEvent", "qpickevent.h", "Qt3DRender", bindCtors: false, owned: false),
            R("Qt3DRender::QPickingProxy", "qpickingproxy.h", "Qt3DRender"),
            R("Qt3DRender::QPickingSettings", "qpickingsettings.h", "Qt3DRender"),
            R("Qt3DRender::QPickLineEvent", "qpicklineevent.h", "Qt3DRender", bindCtors: false, owned: false),
            R("Qt3DRender::QPickPointEvent", "qpickpointevent.h", "Qt3DRender", bindCtors: false, owned: false),
            R("Qt3DRender::QPickTriangleEvent", "qpicktriangleevent.h", "Qt3DRender", bindCtors: false, owned: false),
            R("Qt3DRender::QPointLight", "qpointlight.h", "Qt3DRender"),
            R("Qt3DRender::QPointSize", "qpointsize.h", "Qt3DRender"),
            R("Qt3DRender::QPolygonOffset", "qpolygonoffset.h", "Qt3DRender"),
            R("Qt3DRender::QProximityFilter", "qproximityfilter.h", "Qt3DRender"),
            R("Qt3DRender::QRasterMode", "qrastermode.h", "Qt3DRender"),
            R("Qt3DRender::QRayCaster", "qraycaster.h", "Qt3DRender"),
            R("Qt3DRender::QRayCasterHit", "qraycasterhit.h", "Qt3DRender"),
            R("Qt3DRender::QRenderAspect", "qrenderaspect.h", "Qt3DRender"),
            R("Qt3DRender::QRenderCapabilities", "qrendercapabilities.h", "Qt3DRender"),
            R("Qt3DRender::QRenderCapture", "qrendercapture.h", "Qt3DRender"),
            R("Qt3DRender::QRenderCaptureReply", "qrendercapture.h", "Qt3DRender"),
            R("Qt3DRender::QRenderPass", "qrenderpass.h", "Qt3DRender"),
            R("Qt3DRender::QRenderPassFilter", "qrenderpassfilter.h", "Qt3DRender"),
            R("Qt3DRender::QRenderSettings", "qrendersettings.h", "Qt3DRender"),
            R("Qt3DRender::QRenderState", "qrenderstate.h", "Qt3DRender"),
            R("Qt3DRender::QRenderStateSet", "qrenderstateset.h", "Qt3DRender"),
            R("Qt3DRender::QRenderSurfaceSelector", "qrendersurfaceselector.h", "Qt3DRender"),
            R("Qt3DRender::QRenderTarget", "qrendertarget.h", "Qt3DRender"),
            R("Qt3DRender::QRenderTargetOutput", "qrendertargetoutput.h", "Qt3DRender"),
            R("Qt3DRender::QRenderTargetSelector", "qrendertargetselector.h", "Qt3DRender"),
            R("Qt3DRender::QSceneLoader", "qsceneloader.h", "Qt3DRender"),
            R("Qt3DRender::QScissorTest", "qscissortest.h", "Qt3DRender"),
            R("Qt3DRender::QScreenRayCaster", "qscreenraycaster.h", "Qt3DRender"),
            R("Qt3DRender::QSeamlessCubemap", "qseamlesscubemap.h", "Qt3DRender"),
            R("Qt3DRender::QSetFence", "qsetfence.h", "Qt3DRender"),
            R("Qt3DRender::QShaderData", "qshaderdata.h", "Qt3DRender"),
            R("Qt3DRender::QShaderImage", "qshaderimage.h", "Qt3DRender"),
            R("Qt3DRender::QShaderProgram", "qshaderprogram.h", "Qt3DRender"),
            R("Qt3DRender::QShaderProgramBuilder", "qshaderprogrambuilder.h", "Qt3DRender"),
            R("Qt3DRender::QSharedGLTexture", "qtexture.h", "Qt3DRender"),
            R("Qt3DRender::QSortPolicy", "qsortpolicy.h", "Qt3DRender"),
            R("Qt3DRender::QSpotLight", "qspotlight.h", "Qt3DRender"),
            R("Qt3DRender::QStencilMask", "qstencilmask.h", "Qt3DRender"),
            R("Qt3DRender::QStencilOperation", "qstenciloperation.h", "Qt3DRender"),
            R("Qt3DRender::QStencilOperationArguments", "qstenciloperationarguments.h", "Qt3DRender"),
            R("Qt3DRender::QStencilTest", "qstenciltest.h", "Qt3DRender"),
            R("Qt3DRender::QStencilTestArguments", "qstenciltestarguments.h", "Qt3DRender"),
            R("Qt3DRender::QSubtreeEnabler", "qsubtreeenabler.h", "Qt3DRender"),
            R("Qt3DRender::QTechnique", "qtechnique.h", "Qt3DRender"),
            R("Qt3DRender::QTechniqueFilter", "qtechniquefilter.h", "Qt3DRender"),
            R("Qt3DRender::QTexture1D", "qtexture.h", "Qt3DRender"),
            R("Qt3DRender::QTexture1DArray", "qtexture.h", "Qt3DRender"),
            R("Qt3DRender::QTexture2D", "qtexture.h", "Qt3DRender"),
            R("Qt3DRender::QTexture2DArray", "qtexture.h", "Qt3DRender"),
            R("Qt3DRender::QTexture2DMultisample", "qtexture.h", "Qt3DRender"),
            R("Qt3DRender::QTexture2DMultisampleArray", "qtexture.h", "Qt3DRender"),
            R("Qt3DRender::QTexture3D", "qtexture.h", "Qt3DRender"),
            R("Qt3DRender::QTextureBuffer", "qtexture.h", "Qt3DRender"),
            R("Qt3DRender::QTextureCubeMap", "qtexture.h", "Qt3DRender"),
            R("Qt3DRender::QTextureCubeMapArray", "qtexture.h", "Qt3DRender"),
            R("Qt3DRender::QTextureData", "qtexturedata.h", "Qt3DRender"),
            R("Qt3DRender::QTextureDataUpdate", "qtexturedataupdate.h", "Qt3DRender"),
            R("Qt3DRender::QTextureImage", "qtextureimage.h", "Qt3DRender"),
            R("Qt3DRender::QTextureImageData", "qtextureimagedata.h", "Qt3DRender"),
            R("Qt3DRender::QTextureImageDataGenerator", "qtextureimagedatagenerator.h", "Qt3DRender"),
            R("Qt3DRender::QTextureLoader", "qtexture.h", "Qt3DRender"),
            R("Qt3DRender::QTextureRectangle", "qtexture.h", "Qt3DRender"),
            R("Qt3DRender::QTextureWrapMode", "qtexturewrapmode.h", "Qt3DRender"),
            R("Qt3DRender::QViewport", "qviewport.h", "Qt3DRender"),
            R("Qt3DRender::QWaitFence", "qwaitfence.h", "Qt3DRender"),
        },
    };

    static ExtensionModule Qt3DInputModule() => new ExtensionModule
    {
        Name = "qt3dinput",
        Namespace = "QtSharp.Qt3DInput",
        Assembly = "QtSharp.Qt3DInput",
        Package = "QtSharp.Qt3D",
        NativeLibrary = "QtSharp.Qt3DInput.Native",
        CppNamespaces = new[] { "Qt3DInput" },
        QtModules = new[] { "Qt3DInput" },
        QtDefines = new[] { "QT3DINPUT_LIB" },
        QtLibs = new[] { "Qt63DInput", "Qt63DCore" },
        DependsOn = new[] {"qtcore", "qtgui", "qt3dcore"},
        Roots = new[]
        {
            R("Qt3DInput::QAbstractActionInput", "qabstractactioninput.h", "Qt3DInput"),
            R("Qt3DInput::QAbstractAxisInput", "qabstractaxisinput.h", "Qt3DInput"),
            R("Qt3DInput::QAbstractPhysicalDevice", "qabstractphysicaldevice.h", "Qt3DInput"),
            R("Qt3DInput::QAction", "qaction.h", "Qt3DInput"),
            R("Qt3DInput::QActionInput", "qactioninput.h", "Qt3DInput"),
            R("Qt3DInput::QAnalogAxisInput", "qanalogaxisinput.h", "Qt3DInput"),
            R("Qt3DInput::QAxis", "qaxis.h", "Qt3DInput"),
            R("Qt3DInput::QAxisAccumulator", "qaxisaccumulator.h", "Qt3DInput"),
            R("Qt3DInput::QAxisSetting", "qaxissetting.h", "Qt3DInput"),
            R("Qt3DInput::QButtonAxisInput", "qbuttonaxisinput.h", "Qt3DInput"),
            R("Qt3DInput::QInputAspect", "qinputaspect.h", "Qt3DInput"),
            R("Qt3DInput::QInputChord", "qinputchord.h", "Qt3DInput"),
            R("Qt3DInput::QInputSequence", "qinputsequence.h", "Qt3DInput"),
            R("Qt3DInput::QInputSettings", "qinputsettings.h", "Qt3DInput"),
            R("Qt3DInput::QKeyboardDevice", "qkeyboarddevice.h", "Qt3DInput"),
            R("Qt3DInput::QKeyboardHandler", "qkeyboardhandler.h", "Qt3DInput"),
            R("Qt3DInput::QKeyEvent", "qkeyevent.h", "Qt3DInput", bindCtors: false, owned: false),
            R("Qt3DInput::QLogicalDevice", "qlogicaldevice.h", "Qt3DInput"),
            R("Qt3DInput::QMouseDevice", "qmousedevice.h", "Qt3DInput"),
            R("Qt3DInput::QMouseEvent", "qmouseevent.h", "Qt3DInput", bindCtors: false, owned: false),
            R("Qt3DInput::QMouseHandler", "qmousehandler.h", "Qt3DInput"),
            R("Qt3DInput::QWheelEvent", "qmouseevent.h", "Qt3DInput", bindCtors: false, owned: false),
        },
    };

    static ExtensionModule Qt3DLogicModule() => new ExtensionModule
    {
        Name = "qt3dlogic",
        Namespace = "QtSharp.Qt3DLogic",
        Assembly = "QtSharp.Qt3DLogic",
        Package = "QtSharp.Qt3D",
        NativeLibrary = "QtSharp.Qt3DLogic.Native",
        CppNamespaces = new[] { "Qt3DLogic" },
        QtModules = new[] { "Qt3DLogic" },
        QtDefines = new[] { "QT3DLOGIC_LIB" },
        QtLibs = new[] { "Qt63DLogic", "Qt63DCore" },
        DependsOn = new[] {"qtcore", "qt3dcore"},
        Roots = new[]
        {
            R("Qt3DLogic::QFrameAction", "qframeaction.h", "Qt3DLogic"),
            R("Qt3DLogic::QLogicAspect", "qlogicaspect.h", "Qt3DLogic"),
        },
    };

    static ExtensionModule Qt3DAnimationModule() => new ExtensionModule
    {
        Name = "qt3danimation",
        Namespace = "QtSharp.Qt3DAnimation",
        Assembly = "QtSharp.Qt3DAnimation",
        Package = "QtSharp.Qt3D",
        NativeLibrary = "QtSharp.Qt3DAnimation.Native",
        CppNamespaces = new[] { "Qt3DAnimation" },
        QtModules = new[] { "Qt3DAnimation" },
        QtDefines = new[] { "QT3DANIMATION_LIB" },
        QtLibs = new[] { "Qt63DAnimation", "Qt63DRender", "Qt63DCore" },
        DependsOn = new[] {"qtcore", "qtgui", "qt3dcore", "qt3drender"},
        Roots = new[]
        {
            R("Qt3DAnimation::QAbstractAnimation", "qabstractanimation.h", "Qt3DAnimation"),
            R("Qt3DAnimation::QAbstractAnimationClip", "qabstractanimationclip.h", "Qt3DAnimation"),
            R("Qt3DAnimation::QAbstractChannelMapping", "qabstractchannelmapping.h", "Qt3DAnimation"),
            R("Qt3DAnimation::QAbstractClipAnimator", "qabstractclipanimator.h", "Qt3DAnimation"),
            R("Qt3DAnimation::QAbstractClipBlendNode", "qabstractclipblendnode.h", "Qt3DAnimation"),
            R("Qt3DAnimation::QAdditiveClipBlend", "qadditiveclipblend.h", "Qt3DAnimation"),
            R("Qt3DAnimation::QAnimationAspect", "qanimationaspect.h", "Qt3DAnimation"),
            R("Qt3DAnimation::QAnimationClip", "qanimationclip.h", "Qt3DAnimation"),
            R("Qt3DAnimation::QAnimationClipData", "qanimationclipdata.h", "Qt3DAnimation"),
            R("Qt3DAnimation::QAnimationClipLoader", "qanimationcliploader.h", "Qt3DAnimation"),
            R("Qt3DAnimation::QAnimationController", "qanimationcontroller.h", "Qt3DAnimation"),
            R("Qt3DAnimation::QAnimationGroup", "qanimationgroup.h", "Qt3DAnimation"),
            R("Qt3DAnimation::QBlendedClipAnimator", "qblendedclipanimator.h", "Qt3DAnimation"),
            R("Qt3DAnimation::QCallbackMapping", "qcallbackmapping.h", "Qt3DAnimation"),
            R("Qt3DAnimation::QChannel", "qchannel.h", "Qt3DAnimation"),
            R("Qt3DAnimation::QChannelComponent", "qchannelcomponent.h", "Qt3DAnimation"),
            R("Qt3DAnimation::QChannelMapper", "qchannelmapper.h", "Qt3DAnimation"),
            R("Qt3DAnimation::QChannelMapping", "qchannelmapping.h", "Qt3DAnimation"),
            R("Qt3DAnimation::QClipAnimator", "qclipanimator.h", "Qt3DAnimation"),
            R("Qt3DAnimation::QClipBlendValue", "qclipblendvalue.h", "Qt3DAnimation"),
            R("Qt3DAnimation::QClock", "qclock.h", "Qt3DAnimation"),
            R("Qt3DAnimation::QKeyFrame", "qkeyframe.h", "Qt3DAnimation"),
            R("Qt3DAnimation::QKeyframeAnimation", "qkeyframeanimation.h", "Qt3DAnimation"),
            R("Qt3DAnimation::QLerpClipBlend", "qlerpclipblend.h", "Qt3DAnimation"),
            R("Qt3DAnimation::QMorphingAnimation", "qmorphinganimation.h", "Qt3DAnimation"),
            R("Qt3DAnimation::QMorphTarget", "qmorphtarget.h", "Qt3DAnimation"),
            R("Qt3DAnimation::QSkeletonMapping", "qskeletonmapping.h", "Qt3DAnimation"),
            R("Qt3DAnimation::QVertexBlendAnimation", "qvertexblendanimation.h", "Qt3DAnimation"),
        },
    };

    static ExtensionModule Qt3DExtrasModule() => new ExtensionModule
    {
        Name = "qt3dextras",
        Namespace = "QtSharp.Qt3DExtras",
        Assembly = "QtSharp.Qt3DExtras",
        Package = "QtSharp.Qt3D",
        NativeLibrary = "QtSharp.Qt3DExtras.Native",
        CppNamespaces = new[] { "Qt3DExtras" },
        QtModules = new[] { "Qt3DExtras" },
        QtDefines = new[] { "QT3DEXTRAS_LIB" },
        QtLibs = new[] { "Qt63DExtras", "Qt63DRender", "Qt63DInput", "Qt63DLogic", "Qt63DAnimation", "Qt63DCore" },
        DependsOn = new[] {"qtcore", "qtgui", "qtwidgets", "qtopengl", "qt3dcore", "qt3drender", "qt3dinput", "qt3dlogic", "qt3danimation"},
        Roots = new[]
        {
            R("Qt3DExtras::QAbstractCameraController", "qabstractcameracontroller.h", "Qt3DExtras"),
            R("Qt3DExtras::QAbstractSpriteSheet", "qabstractspritesheet.h", "Qt3DExtras"),
            R("Qt3DExtras::QConeGeometry", "qconegeometry.h", "Qt3DExtras"),
            R("Qt3DExtras::QConeGeometryView", "qconegeometryview.h", "Qt3DExtras"),
            R("Qt3DExtras::QConeMesh", "qconemesh.h", "Qt3DExtras"),
            R("Qt3DExtras::QCuboidGeometry", "qcuboidgeometry.h", "Qt3DExtras"),
            R("Qt3DExtras::QCuboidGeometryView", "qcuboidgeometryview.h", "Qt3DExtras"),
            R("Qt3DExtras::QCuboidMesh", "qcuboidmesh.h", "Qt3DExtras"),
            R("Qt3DExtras::QCylinderGeometry", "qcylindergeometry.h", "Qt3DExtras"),
            R("Qt3DExtras::QCylinderGeometryView", "qcylindergeometryview.h", "Qt3DExtras"),
            R("Qt3DExtras::QCylinderMesh", "qcylindermesh.h", "Qt3DExtras"),
            R("Qt3DExtras::QDiffuseMapMaterial", "qdiffusemapmaterial.h", "Qt3DExtras"),
            R("Qt3DExtras::QDiffuseSpecularMapMaterial", "qdiffusespecularmapmaterial.h", "Qt3DExtras"),
            R("Qt3DExtras::QDiffuseSpecularMaterial", "qdiffusespecularmaterial.h", "Qt3DExtras"),
            R("Qt3DExtras::QExtrudedTextGeometry", "qextrudedtextgeometry.h", "Qt3DExtras"),
            R("Qt3DExtras::QExtrudedTextMesh", "qextrudedtextmesh.h", "Qt3DExtras"),
            R("Qt3DExtras::QFirstPersonCameraController", "qfirstpersoncameracontroller.h", "Qt3DExtras"),
            R("Qt3DExtras::QForwardRenderer", "qforwardrenderer.h", "Qt3DExtras"),
            R("Qt3DExtras::QGoochMaterial", "qgoochmaterial.h", "Qt3DExtras"),
            R("Qt3DExtras::QMetalRoughMaterial", "qmetalroughmaterial.h", "Qt3DExtras"),
            R("Qt3DExtras::QMorphPhongMaterial", "qmorphphongmaterial.h", "Qt3DExtras"),
            R("Qt3DExtras::QNormalDiffuseMapAlphaMaterial", "qnormaldiffusemapalphamaterial.h", "Qt3DExtras"),
            R("Qt3DExtras::QNormalDiffuseMapMaterial", "qnormaldiffusemapmaterial.h", "Qt3DExtras"),
            R("Qt3DExtras::QNormalDiffuseSpecularMapMaterial", "qnormaldiffusespecularmapmaterial.h", "Qt3DExtras"),
            R("Qt3DExtras::QOrbitCameraController", "qorbitcameracontroller.h", "Qt3DExtras"),
            R("Qt3DExtras::QPerVertexColorMaterial", "qpervertexcolormaterial.h", "Qt3DExtras"),
            R("Qt3DExtras::QPhongAlphaMaterial", "qphongalphamaterial.h", "Qt3DExtras"),
            R("Qt3DExtras::QPhongMaterial", "qphongmaterial.h", "Qt3DExtras"),
            R("Qt3DExtras::QPlaneGeometry", "qplanegeometry.h", "Qt3DExtras"),
            R("Qt3DExtras::QPlaneGeometryView", "qplanegeometryview.h", "Qt3DExtras"),
            R("Qt3DExtras::QPlaneMesh", "qplanemesh.h", "Qt3DExtras"),
            R("Qt3DExtras::QSkyboxEntity", "qskyboxentity.h", "Qt3DExtras"),
            R("Qt3DExtras::QSphereGeometry", "qspheregeometry.h", "Qt3DExtras"),
            R("Qt3DExtras::QSphereGeometryView", "qspheregeometryview.h", "Qt3DExtras"),
            R("Qt3DExtras::QSphereMesh", "qspheremesh.h", "Qt3DExtras"),
            R("Qt3DExtras::QSpriteGrid", "qspritegrid.h", "Qt3DExtras"),
            R("Qt3DExtras::QSpriteSheet", "qspritesheet.h", "Qt3DExtras"),
            R("Qt3DExtras::QSpriteSheetItem", "qspritesheetitem.h", "Qt3DExtras"),
            R("Qt3DExtras::Qt3DWindow", "qt3dwindow.h", "Qt3DExtras"),
            R("Qt3DExtras::QText2DEntity", "qtext2dentity.h", "Qt3DExtras"),
            R("Qt3DExtras::QTextureMaterial", "qtexturematerial.h", "Qt3DExtras"),
            R("Qt3DExtras::QTorusGeometry", "qtorusgeometry.h", "Qt3DExtras"),
            R("Qt3DExtras::QTorusGeometryView", "qtorusgeometryview.h", "Qt3DExtras"),
            R("Qt3DExtras::QTorusMesh", "qtorusmesh.h", "Qt3DExtras"),
        },
    };
}
