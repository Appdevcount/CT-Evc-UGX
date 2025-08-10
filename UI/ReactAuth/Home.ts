
import React, { useContext, useEffect } from 'react';
import IdleLogout from '../idle-logout/idle-logout';
import { RequestContext } from '../../state/store/request-store';

function Home () {
    const [state] = useContext<any>(RequestContext);
    
    useEffect( () => {
        (window as any).token = state.accessToken;
    }, [state.accessToken]);

    return (
        <>
            {!state.idleTimeout
                ? 
                <div className="content-wrapper">
                    <div className="container-fluid">
                        <div className="row mt-3">
                            <div className="col-lg-12">
                                <div className="card mb-3">
                                    <div className="card-body p-sm-5 mb-3">
                                        <div className="row">
                                            <div className="col-lg-12">
                                                <div className="row">
                                                    <div className="col-md-12">
                                                        <h1 id="forms" className="text-primary">UCX&nbsp;</h1>
                                                        <h2 className="lead mb-5">
                                                            The UCX will be an application that provides a Universal Clinical Experience regardless of medical disciplines or existing and future platform origin. Unlike having clinicians log into multiple applications, our approach will allow for more efficient and effective processing and an enhanced user experience, which supports our objective to have best-in-class client, provider and patient experiences.<br />
                                                        </h2>
                                                    </div>
                                                </div>
                                            </div>
                                        </div>
                                    </div>
                                </div>
                            </div>
                        </div>
                    </div>

                </div>
                : <IdleLogout />
            }
        </>
    );
}
