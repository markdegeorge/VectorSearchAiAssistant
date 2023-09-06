window.blazorExtensions = {

    saveUserSelection: function (userValue) {
        localStorage.setItem('selectedUser', userValue);
    },

    retrieveUserSelection: function () {
        return localStorage.getItem('selectedUser') || '';
    }

};
