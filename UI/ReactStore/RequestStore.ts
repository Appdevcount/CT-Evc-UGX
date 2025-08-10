import React, {createContext, useReducer} from "react";
import LookupRequest from "../../types/lookup";
import { RequestReducer } from '../reducers/request-reducer';

type Props = {
    children: React.ReactNode
}

const initialState = {
    request: localStorage.getItem("currentRequest") === null ? {} as LookupRequest : JSON.parse(localStorage.getItem("currentRequest")!),
    refresh: false,
    error: null,
    badToken: false,
    accessToken: "",
    idleTimeout: false,
    getNextRequestId: "",
    refreshRequestKey: "",
    originSystem:"",
};
export const RequestContext = createContext<LookupRequest | {}>(initialState);

const RequestStore = ( { children }: Props ) => {
    const [state, dispatch] = useReducer(RequestReducer, initialState);
    return (
        <RequestContext.Provider value={[state, dispatch]} >
            {children}
        </RequestContext.Provider>
    )
};

export default RequestStore;